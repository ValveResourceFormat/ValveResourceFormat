using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// Loads a source map (<c>.vmap</c>) into a scene the way Hammer shows it: meshes are drawn from their
/// polygons with the materials they name, entities are drawn with the helpers their classes declare, and
/// instances and prefabs are expanded into what they place. Nothing of the game runs: entities stay where
/// they were placed and are only linked to the ones they name.
/// </summary>
public sealed class MapDocumentLoader : IDisposable
{
    /// <summary>Visibility layer of the meshes that are in no world layer.</summary>
    public const string WorldLayerName = "World";

    /// <summary>Visibility layer of the entities.</summary>
    public const string EntitiesLayerName = "Entities";

    private readonly Scene scene;
    private readonly RendererContext rendererContext;
    private readonly Dictionary<string, MapDocument?> prefabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MapDocument, int> documentIds = [];
    private readonly Dictionary<DMElement, PolygonMeshRenderData> renderData = [];
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MapEntity> mapEntities = [];
    private readonly HammerEntityVisuals entityVisuals;
    private readonly string? contentRoot;

    // What the editor baked for each node at each place it is put, by the IDs of the nodes it was placed through
    private readonly Dictionary<string, CDmeNodeInstanceData> nodeInstanceData = new(StringComparer.Ordinal);
    private bool hasVertexLighting;

    private CancellationToken CancellationToken => rendererContext.CancellationToken;

    /// <summary>Gets the map being loaded.</summary>
    public MapDocument Document { get; }

    /// <summary>Gets the keyvalues of every entity of the map, the ones of its instances and prefabs included.</summary>
    public List<Entity> Entities { get; } = [];

    /// <summary>Gets every entity of the map where it was placed, with what it is drawn with.</summary>
    public IReadOnlyList<MapEntity> MapEntities => mapEntities;

    /// <summary>
    /// Gets whether the map has a light that is on, once it is loaded. Hammer shows a map without one fullbright,
    /// as it has no light of its own to add.
    /// </summary>
    public bool HasLights { get; private set; }

    /// <summary>
    /// Gets whether the map is lit with lighting the editor baked for it, once it is loaded. It is not when nothing
    /// was baked, or the game does not say which version of baked lighting it reads.
    /// </summary>
    public bool HasBakedLighting => scene.LightingInfo.LightmapVersionNumber != 0 || hasVertexLighting;

    /// <summary>Gets or sets how much of what entities are drawn with is created.</summary>
    public HelperLevel HelperLevel { get; set; } = HelperLevel.All;

    /// <summary>Gets the layers shown until others are chosen.</summary>
    public HashSet<string> DefaultEnabledLayers { get; } = [WorldLayerName, EntitiesLayerName, Scene.ParticlesLayerName];

    /// <summary>Gets or sets where loading phases are reported.</summary>
    public IProgress<string>? LoadingProgress { get; set; }

    /// <summary>Initializes a new instance of the <see cref="MapDocumentLoader"/> class.</summary>
    /// <param name="document">The map to load. The loader does not own it.</param>
    /// <param name="scene">The scene to load the map into.</param>
    /// <param name="entityVisuals">
    /// The editor entities of the view, which give the classes of the map and are told of its entities, so
    /// they are found and drawn as the ones of a compiled map are.
    /// </param>
    public MapDocumentLoader(MapDocument document, Scene scene, HammerEntityVisuals entityVisuals)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entityVisuals);

        Document = document;
        this.scene = scene;
        this.entityVisuals = entityVisuals;
        rendererContext = scene.RendererContext;
        contentRoot = document.FindContentRoot();
        documentIds[document] = 0;

        foreach (var data in document.Root.NodeInstanceData.OfType<CDmeNodeInstanceData>())
        {
            nodeInstanceData.TryAdd(data.Name, data);
        }
    }

    /// <summary>Loads everything the map places into the scene.</summary>
    public void Load()
    {
        LoadingProgress?.Report("Reading map nodes…");

        var walker = new MapNodeWalker(LoadPrefab);
        var placedNodes = walker.Walk(Document).ToList();

        foreach (var placedNode in placedNodes)
        {
            if (placedNode.WorldLayer is { Length: > 0 } worldLayer)
            {
                DefaultEnabledLayers.Add(worldLayer);
            }
        }

        LoadingProgress?.Report("Loading entities…");

        var created = new Dictionary<PlacedMapNode, MapEntity>();

        foreach (var placedNode in placedNodes.Where(static placedNode => placedNode.Node is CMapEntity))
        {
            // A path comes before its nodes, which are the entities under it
            var path = placedNode.Parent is { } parent ? created.GetValueOrDefault(parent) : null;

            if (CreateEntity(placedNode, path) is { } mapEntity)
            {
                created[placedNode] = mapEntity;
            }
        }

        // What the entities bake goes first: models and meshes take into their shaders whether the scene has cubemaps
        // and light probes, and of which kind, as they are built
        foreach (var mapEntity in mapEntities)
        {
            AddToScene(mapEntity, bakeResources: true);
        }

        PackBakedLighting();

        foreach (var mapEntity in mapEntities)
        {
            CancellationToken.ThrowIfCancellationRequested();

            AddToScene(mapEntity, bakeResources: false);
        }

        LoadingProgress?.Report("Loading map geometry…");

        foreach (var placedNode in placedNodes)
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (placedNode.Node is not CMapMesh mesh)
            {
                continue;
            }

            if (placedNode.Parent == null)
            {
                AddMesh(placedNode, mesh, owner: null);
            }
            else if (created.TryGetValue(placedNode.Parent, out var owner))
            {
                AddMesh(placedNode, mesh, owner);
            }
        }

        // The render data is only shared between the placements of a mesh while they are being made
        renderData.Clear();

        LinkEntities();
        ApplyEnvironment();

        var lights = scene.AllNodes.OfType<SceneLight>().ToList();
        scene.LightingInfo.StoreLights(lights);

        // Lights that are off stay in the scene without brightness
        HasLights = lights.Exists(static light => light.Brightness * light.BrightnessScale > 0f);
    }

    // The editor shows every fragment with the cubemaps and light probe volumes it is in, sampling each one's own
    // textures. They are copied into a cube array and an atlas to the same effect.
    private void PackBakedLighting()
    {
        var lighting = scene.LightingInfo;
        var logger = rendererContext.Logger;

        if (EnvironmentMapCache.Create(lighting.EnvMaps, logger) is { } cubemaps)
        {
            lighting.CubemapType = CubemapType.CubemapArray;
            lighting.Lightmaps["g_tEnvironmentMap"] = cubemaps.Texture;
        }

        var volumes = lighting.LightProbes.FindAll(static probe => probe.Irradiance != null);

        if (volumes.Count > 0 && LightProbeAtlas.Create(volumes, logger) != null)
        {
            lighting.LightProbeType = LightProbeType.ProbeAtlas;
        }
    }

    // Every entity exists by now, so the ones that name others can find them
    private void LinkEntities()
    {
        foreach (var mapEntity in mapEntities)
        {
            foreach (var helper in mapEntity.Helpers)
            {
                helper.Link();
            }

            AddConnectionLines(mapEntity);
        }
    }

    // What the whole view is drawn with comes from the entities that are the globals of the map, and the
    // cubemap fog from the first cubemap fog entity that is not disabled at the start
    private void ApplyEnvironment()
    {
        var globals = FindGlobals();
        var fog = LinearFog.None;

        // The first helper of the fog global that gives a fog gives it
        if (globals.GetValueOrDefault("fog") is { } fogEntity)
        {
            foreach (var source in fogEntity.Helpers.OfType<ILinearFogSource>())
            {
                if (source.TryGetFog(ref fog))
                {
                    break;
                }
            }
        }

        scene.FogInfo.LinearFog = fog;

        globals.GetValueOrDefault("gradient_fog")?.Helpers.OfType<GradientFogHelper>().FirstOrDefault()?.Apply();
        globals.GetValueOrDefault("tonemap")?.Helpers.OfType<ToneMappingHelper>().FirstOrDefault()?.Apply();

        var cubemapFog = mapEntities.Find(static mapEntity => mapEntity.Classname.Equals("env_cubemap_fog", StringComparison.OrdinalIgnoreCase)
            && EnvironmentKeys.ParseInteger(mapEntity.KeyValues.GetStringProperty("startdisabled") ?? string.Empty, 0) == 0);

        cubemapFog?.Helpers.OfType<CubemapFogHelper>().FirstOrDefault()?.Apply();

        if (globals.GetValueOrDefault("map_parameters") is { KeyValues: var parameters })
        {
            scene.EnvironmentWetness = new Vector4(
                parameters.GetFloatProperty("envwetnesscoverage", 1f),
                parameters.GetFloatProperty("envwetnessdryingamount", 0f),
                parameters.GetFloatProperty("envrainstrength", 1f),
                parameters.GetFloatProperty("envpuddleripplestrength", 1f));

            scene.PuddleWindDirection = parameters.GetFloatProperty("envpuddlerippledirection", 0f);
        }
    }

    // Each global is the entity that is on at the fewest prefabs deep, of those whose classes declare it.
    // They are taken in the order the classes are declared in, and then in the order of the map.
    private Dictionary<string, MapEntity> FindGlobals()
    {
        var fgd = entityVisuals.Fgd;
        var classOrder = new Dictionary<FgdClass, int>();

        foreach (var gameClass in fgd.Classes.Concat(fgd.Fallback?.Classes ?? []))
        {
            classOrder.TryAdd(gameClass, classOrder.Count);
        }

        var globals = new Dictionary<string, MapEntity>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var candidates = mapEntities.OrderBy(mapEntity => mapEntity.FgdClass is { } gameClass ? classOrder.GetValueOrDefault(gameClass, int.MaxValue) : int.MaxValue);

        foreach (var mapEntity in candidates)
        {
            var depth = mapEntity.PlacedNode.PrefabDepth;

            foreach (var global in mapEntity.Helpers.OfType<GlobalHelper>())
            {
                if (!global.IsEnabled)
                {
                    continue;
                }

                foreach (var name in global.Names)
                {
                    if (!depths.TryGetValue(name, out var bestDepth) || depth < bestDepth)
                    {
                        globals[name] = mapEntity;
                        depths[name] = depth;
                    }
                }
            }
        }

        return globals;
    }

    // A line from the entity to every entity its entity I/O connections reach
    private void AddConnectionLines(MapEntity mapEntity)
    {
        if (mapEntity.KeyValues.Connections is not { } connections)
        {
            return;
        }

        var start = mapEntity.Transform.Translation;
        var alreadySeen = new HashSet<IHelperOwner>();

        foreach (var connection in connections)
        {
            var targets = connection.TargetName.Equals("!self", StringComparison.OrdinalIgnoreCase)
                ? [mapEntity]
                : mapEntity.FindByName(connection.TargetName);

            foreach (var target in targets)
            {
                if (alreadySeen.Add(target))
                {
                    mapEntity.AddNode(HammerEntityVisuals.CreateConnectionLine(scene, start, target.Transform.Translation));
                }
            }
        }
    }

    private MapDocument? LoadPrefab(MapDocument owner, string targetMapPath)
    {
        if (contentRoot == null)
        {
            return null;
        }

        var path = Path.GetFullPath(Path.Combine(contentRoot, targetMapPath));

        if (prefabs.TryGetValue(path, out var prefab))
        {
            return prefab;
        }

        try
        {
            if (File.Exists(path))
            {
                prefab = MapDocument.Load(path);
                documentIds[prefab] = documentIds.Count;
            }
            else
            {
                rendererContext.Logger.LogWarning("Prefab map \"{Path}\" does not exist", targetMapPath);
            }
        }
        catch (Exception e) when (e is IOException or InvalidDataException)
        {
            rendererContext.Logger.LogWarning(e, "Failed to load prefab map \"{Path}\"", targetMapPath);
        }

        prefabs[path] = prefab;
        return prefab;
    }

    private MapEntity? CreateEntity(PlacedMapNode placedNode, MapEntity? path)
    {
        if (placedNode.CreateEntity() is not { } data)
        {
            return null;
        }

        Entities.Add(data);

        var mapEntity = new MapEntity(scene, placedNode, data, placedNode.WorldLayer ?? EntitiesLayerName, entityVisuals.Fgd, mapEntities, entitiesByName, path);

        mapEntity.CreateHelpers(HelperLevel);
        mapEntities.Add(mapEntity);
        entityVisuals.AddMapEntity(mapEntity);

        if (data.GetStringProperty("targetname") is { Length: > 0 } name)
        {
            if (!entitiesByName.TryGetValue(name, out var named))
            {
                entitiesByName[name] = named = [];
            }

            named.Add(mapEntity);
        }

        return mapEntity;
    }

    // Either only the helpers that add what the entity bakes, or only the others
    private void AddToScene(MapEntity mapEntity, bool bakeResources)
    {
        foreach (var helper in mapEntity.Helpers)
        {
            if (helper is BakeResourceHelper != bakeResources)
            {
                continue;
            }

            try
            {
                helper.AddToScene();
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                // A map being edited can name things that are not compiled yet, which must not stop the rest of it
                rendererContext.Logger.LogWarning(e, "Failed to draw the {Helper} of entity '{Classname}' (node {NodeId})",
                    helper.Declaration.Name, mapEntity.Classname, (mapEntity.PlacedNode.Node as MapNode)?.NodeID);
            }
        }
    }

    private void AddMesh(PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner)
    {
        PolygonMeshRenderData? meshRenderData;
        var vertexLighting = GetVertexLighting(placedNode, mesh);

        // Lighting is baked for each place a mesh is put, so a lit mesh is drawn from data of its own
        if (vertexLighting != null)
        {
            meshRenderData = new PolygonMeshRenderData(mesh.Mesh, vertexLighting);
            hasVertexLighting = true;
        }
        else if (!renderData.TryGetValue(mesh, out meshRenderData))
        {
            meshRenderData = new PolygonMeshRenderData(mesh.Mesh);
            renderData[mesh] = meshRenderData;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_mesh_{documentIds[placedNode.Document]}_{mesh.NodeID}");

        var shaderArguments = scene.LightingInfo.CreateShaderArguments(hasVertexLighting: vertexLighting != null);

        // The faces of each material are a mesh of their own, with the vertex streams that material reads
        for (var i = 0; i < meshRenderData.MaterialGroups.Length; i++)
        {
            var group = meshRenderData.MaterialGroups[i];
            var groupName = string.Create(CultureInfo.InvariantCulture, $"{name}_{i}");
            var material = rendererContext.MaterialLoader.GetMaterial(group.Material.Length > 0 ? group.Material : null, shaderArguments);
            var (vbib, bounds) = PolygonMeshRenderData.BuildVertexAndIndexBuffers(group, material.Material.InputSignature, Matrix4x4.Identity);

            var node = new MeshSceneNode(scene, RenderableMesh.CreateMesh(groupName, material, vbib, bounds, rendererContext, sortByMaterial: true))
            {
                Name = groupName,
                LayerName = placedNode.WorldLayer ?? (owner == null ? WorldLayerName : EntitiesLayerName),
                TintAlpha = placedNode.Tint * new Vector4(mesh.TintColor.R, mesh.TintColor.G, mesh.TintColor.B, mesh.RenderAmount) / 255f,
                OverlayRenderOrder = mesh is CMapStaticOverlay overlay ? overlay.RenderOrder : 0,
                Transform = placedNode.Transform,
                EntityData = owner?.KeyValues,
            };

            if (IsShadowless(material))
            {
                node.Flags |= ObjectTypeFlags.NoShadows;
            }

            scene.Add(node, dynamic: false);
            owner?.AddMeshNode(node);
        }
    }

    // The lighting the editor baked for the vertices of a mesh where it is put, as streams that replace the ones of the mesh
    private PolygonMeshStream[]? GetVertexLighting(PlacedMapNode placedNode, CMapMesh mesh)
    {
        if (mesh is CMapStaticOverlay)
        {
            return null;
        }

        var path = string.Join('.', placedNode.NodeIdPath.Select(static id => id.ToString(CultureInfo.InvariantCulture)));

        return nodeInstanceData.TryGetValue(path, out var data) ? data.CreateVertexLightingStreams(mesh.Mesh) : null;
    }

    // Faces that are not drawn in the compiled map, such as the sky box around it, and occluders cast no
    // shadows, unless they are there to block light
    private static bool IsShadowless(RenderMaterial material)
        => !material.BlocksLight
            && (material.Material.IntAttributes.GetValueOrDefault("mapbuilder.nodraw") != 0
                || material.Material.IntAttributes.GetValueOrDefault("mapbuilder.occluder") != 0);

    /// <summary>Releases the prefab maps the loader opened.</summary>
    public void Dispose()
    {
        foreach (var prefab in prefabs.Values)
        {
            prefab?.Dispose();
        }

        prefabs.Clear();
    }
}

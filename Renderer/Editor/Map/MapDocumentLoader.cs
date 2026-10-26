using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

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
    private readonly Dictionary<CMapMesh, PolygonMeshRenderData> renderData = [];
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName = new(StringComparer.OrdinalIgnoreCase);
    private readonly List<MapEntity> mapEntities = [];
    private readonly HammerEntityVisuals entityVisuals;
    private readonly string? contentRoot;

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
    public bool HasBakedLighting => scene.LightingInfo.LightmapVersionNumber != 0;

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
    }

    /// <summary>Loads everything the map places into the scene.</summary>
    public void Load()
    {
        LoadingProgress?.Report("Reading map nodes…");

        var walker = new MapNodeWalker((_, targetMapPath) => LoadPrefab(targetMapPath));
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

        foreach (var placedNode in placedNodes)
        {
            if (placedNode.Node is not CMapEntity)
            {
                continue;
            }

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

        foreach (var mapEntity in mapEntities)
        {
            CancellationToken.ThrowIfCancellationRequested();

            AddToScene(mapEntity, bakeResources: false);
        }

        LoadingProgress?.Report("Loading map geometry…");

        foreach (var placedNode in placedNodes)
        {
            CancellationToken.ThrowIfCancellationRequested();

            MapEntity? owner = null;

            // Static overlays are only drawn where they are projected onto the faces under them
            if (placedNode.Node is not CMapMesh mesh || mesh is CMapStaticOverlay
                || (placedNode.Parent != null && !created.TryGetValue(placedNode.Parent, out owner)))
            {
                continue;
            }

            AddMesh(placedNode, mesh, owner);
        }

        // The render data is only shared between the placements of a mesh while they are being made
        renderData.Clear();

        LinkEntities();
        ApplyEnvironment();

        var lights = new List<SceneLight>();

        foreach (var node in scene.AllNodes)
        {
            if (node is SceneLight light)
            {
                lights.Add(light);
            }
        }

        scene.LightingInfo.StoreLights(lights);

        // Lights that are off stay in the scene without brightness
        HasLights = lights.Exists(static light => light.Brightness * light.BrightnessScale > 0f);
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

        // The first helper of the fog global that gives a fog gives it
        scene.FogInfo.LinearFog = FindHelper<ILinearFogSource>(globals.GetValueOrDefault("fog"))?.GetFog() ?? LinearFog.None;

        FindHelper<GradientFogHelper>(globals.GetValueOrDefault("gradient_fog"))?.Apply();
        FindHelper<ToneMappingHelper>(globals.GetValueOrDefault("tonemap"))?.Apply();

        var cubemapFog = mapEntities.Find(static mapEntity => mapEntity.Classname.Equals("env_cubemap_fog", StringComparison.OrdinalIgnoreCase)
            && EnvironmentKeys.ParseInteger(mapEntity.KeyValues.GetStringProperty("startdisabled") ?? string.Empty, 0) == 0);

        FindHelper<CubemapFogHelper>(cubemapFog)?.Apply();

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

    private static T? FindHelper<T>(MapEntity? mapEntity) where T : class
    {
        if (mapEntity == null)
        {
            return null;
        }

        foreach (var helper in mapEntity.Helpers)
        {
            if (helper is T found)
            {
                return found;
            }
        }

        return null;
    }

    // Each global is the entity that is on at the fewest prefabs deep, of those whose classes declare it.
    // They are taken in the order the classes are declared in, and then in the order of the map.
    private Dictionary<string, MapEntity> FindGlobals()
    {
        var classOrder = new Dictionary<EntityClass, int>();

        foreach (var entityClass in entityVisuals.Classes.Classes)
        {
            classOrder.Add(entityClass, classOrder.Count);
        }

        var globals = new Dictionary<string, MapEntity>(StringComparer.OrdinalIgnoreCase);
        var depths = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);

        var candidates = mapEntities.OrderBy(mapEntity => mapEntity.EntityClass is { } entityClass ? classOrder.GetValueOrDefault(entityClass, int.MaxValue) : int.MaxValue);

        foreach (var mapEntity in candidates)
        {
            var depth = mapEntity.PlacedNode.PrefabDepth;

            foreach (var helper in mapEntity.Helpers)
            {
                if (helper is not GlobalHelper { IsEnabled: true } global)
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
    private static void AddConnectionLines(MapEntity mapEntity)
    {
        if (mapEntity.KeyValues.Connections is { } connections)
        {
            HammerEntityVisuals.AddConnectionLines(mapEntity, connections, connection => connection.TargetName.Equals("!self", StringComparison.OrdinalIgnoreCase)
                ? [mapEntity]
                : mapEntity.FindByName(connection.TargetName));
        }
    }

    // Prefabs name their maps relative to the content root, wherever the prefab placing them is
    private MapDocument? LoadPrefab(string targetMapPath)
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

        var mapEntity = new MapEntity(scene, placedNode, data, placedNode.WorldLayer ?? EntitiesLayerName, entityVisuals.Classes, mapEntities, entitiesByName, path);

        mapEntity.CreateHelpers();
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
        if (!renderData.TryGetValue(mesh, out var meshRenderData))
        {
            meshRenderData = new PolygonMeshRenderData(mesh.Mesh);
            renderData[mesh] = meshRenderData;
        }

        // Every placement of a mesh is drawn from the same buffers
        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_mesh_{documentIds[placedNode.Document]}_{mesh.NodeID}");
        var shaderArguments = scene.LightingInfo.CreateShaderArguments();
        var mapMesh = new MapMesh(placedNode, mesh, owner);

        owner?.AddMesh(mapMesh);

        // The faces of each material are a mesh of their own, with the vertex streams that material reads
        for (var i = 0; i < meshRenderData.MaterialGroups.Length; i++)
        {
            var group = meshRenderData.MaterialGroups[i];
            var material = GetMaterial(group, owner, shaderArguments);
            var nodeName = string.Create(CultureInfo.InvariantCulture, $"{name}_{i}");

            mapMesh.AddNode(AddMeshNode(nodeName, material, meshRenderData.GetBuffers(i, material.Material.InputSignature), placedNode, mesh, owner));
        }
    }

    // The class of the entity a mesh is tied to can draw all of it with one material, leaving its faces as they are
    private RenderMaterial GetMaterial(PolygonMesh.BakedSubmesh group, MapEntity? owner, Dictionary<string, byte> shaderArguments)
        => rendererContext.MaterialLoader.GetMaterial(owner?.EntityClass?.AutoApplyMaterial ?? (group.Material.Length > 0 ? group.Material : null), shaderArguments);

    private MeshSceneNode AddMeshNode(string name, RenderMaterial material, (VBIB Buffers, AABB Bounds) geometry, PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner)
    {
        var renderableMesh = RenderableMesh.CreateMesh(name, material, geometry.Buffers, geometry.Bounds, rendererContext, sortByMaterial: true);
        var node = new MeshSceneNode(scene, renderableMesh)
        {
            Name = name,
            LayerName = placedNode.WorldLayer ?? (owner == null ? WorldLayerName : EntitiesLayerName),
            TintAlpha = placedNode.Tint * new Vector4(mesh.TintColor.R, mesh.TintColor.G, mesh.TintColor.B, mesh.RenderAmount) / 255f,
            Transform = placedNode.Transform,
            EntityData = owner?.KeyValues,
        };

        if (IsShadowless(material))
        {
            node.Flags |= ObjectTypeFlags.NoShadows;
        }

        scene.Add(node, dynamic: false);

        return node;
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

using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
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

        // Lighting volumes go first: meshes copy the scene's render attributes into their shader combos as they
        // are built, and some of those come from these entities
        var placedEntities = placedNodes
            // The nodes of a path are flattened into the keys of the path
            .Where(static placedNode => placedNode.Node is CMapEntity and not CMapPathNode)
            .OrderByDescending(static placedNode => IsCubemapOrProbe(placedNode.Node));

        var created = new Dictionary<PlacedMapNode, MapEntity>();

        foreach (var placedNode in placedEntities)
        {
            if (CreateEntity(placedNode) is { } mapEntity)
            {
                created[placedNode] = mapEntity;
            }
        }

        foreach (var mapEntity in mapEntities)
        {
            CancellationToken.ThrowIfCancellationRequested();

            AddToScene(mapEntity);
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

    // What the whole view is drawn with is taken from one entity of each kind: the first that is not off
    private void ApplyEnvironment()
    {
        var helpers = mapEntities.SelectMany(static mapEntity => mapEntity.Helpers).ToList();

        helpers.OfType<GradientFogHelper>().FirstOrDefault(static fog => !fog.StartsDisabled)?.Apply();
        helpers.OfType<CubemapFogHelper>().FirstOrDefault(static fog => !fog.StartsDisabled)?.Apply();

        if (mapEntities.Find(static mapEntity => mapEntity.Classname == "info_map_parameters") is { KeyValues: var parameters })
        {
            scene.EnvironmentWetness = new Vector4(
                parameters.GetFloatProperty("envwetnesscoverage", 1f),
                parameters.GetFloatProperty("envwetnessdryingamount", 0f),
                parameters.GetFloatProperty("envrainstrength", 1f),
                parameters.GetFloatProperty("envpuddleripplestrength", 1f));

            scene.PuddleWindDirection = parameters.GetFloatProperty("envpuddlerippledirection", 0f);
        }
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

    private static bool IsCubemapOrProbe(DMElement node)
        => node is CMapEntity entity && entity.EntityProperties.Get<string>("classname")
            is "env_combined_light_probe_volume" or "env_light_probe_volume" or "env_cubemap_box" or "env_cubemap";

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

    private MapEntity? CreateEntity(PlacedMapNode placedNode)
    {
        if (placedNode.CreateEntity() is not { } data)
        {
            return null;
        }

        Entities.Add(data);

        var mapEntity = new MapEntity(scene, placedNode, data, placedNode.WorldLayer ?? EntitiesLayerName, entityVisuals.Fgd, mapEntities, entitiesByName);

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

    private void AddToScene(MapEntity mapEntity)
    {
        foreach (var helper in mapEntity.Helpers)
        {
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

        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_mesh_{documentIds[placedNode.Document]}_{mesh.NodeID}");

        var shaderArguments = scene.LightingInfo.CreateShaderArguments();

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
        }
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

using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;
using BaseEntity = ValveResourceFormat.Renderer.Entities.BaseEntity;
using DMElement = Datamodel.Element;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// Loads a source map (<c>.vmap</c>) into a scene the way Hammer shows it: meshes are drawn from their
/// polygons with the materials they name, entities spawn from their keyvalues, and instances and prefabs
/// are expanded into what they place.
/// </summary>
public sealed class MapDocumentLoader : IDisposable
{
    /// <summary>Visibility layer of the meshes that are in no world layer.</summary>
    public const string WorldLayerName = "World";

    /// <summary>Visibility layer of the entities.</summary>
    public const string EntitiesLayerName = "Entities";

    private readonly Scene scene;
    private readonly EntitySystem entitySystem;
    private readonly RendererContext rendererContext;
    private readonly Dictionary<string, MapDocument?> prefabs = new(StringComparer.OrdinalIgnoreCase);
    private readonly Dictionary<MapDocument, int> documentIds = [];
    private readonly Dictionary<DMElement, PolygonMeshRenderData> renderData = [];
    private readonly string? contentRoot;

    private CancellationToken CancellationToken => rendererContext.CancellationToken;

    /// <summary>Gets the map being loaded.</summary>
    public MapDocument Document { get; }

    /// <summary>Gets the keyvalues of every entity the map spawned, the ones of its instances and prefabs included.</summary>
    public List<Entity> Entities { get; } = [];

    /// <summary>Gets the layers shown until others are chosen.</summary>
    public HashSet<string> DefaultEnabledLayers { get; } = [WorldLayerName, EntitiesLayerName, Scene.ParticlesLayerName];

    /// <summary>Gets or sets where loading phases are reported.</summary>
    public IProgress<string>? LoadingProgress { get; set; }

    /// <summary>Initializes a new instance of the <see cref="MapDocumentLoader"/> class.</summary>
    /// <param name="document">The map to load. The loader does not own it.</param>
    /// <param name="scene">The scene to load the map into.</param>
    /// <param name="entitySystem">The entity world the map's entities spawn into.</param>
    public MapDocumentLoader(MapDocument document, Scene scene, EntitySystem entitySystem)
    {
        ArgumentNullException.ThrowIfNull(document);
        ArgumentNullException.ThrowIfNull(scene);
        ArgumentNullException.ThrowIfNull(entitySystem);

        Document = document;
        this.scene = scene;
        this.entitySystem = entitySystem;
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

        entitySystem.SpawnWorld(scene);

        LoadingProgress?.Report("Loading entities…");

        // Cubemaps and probes spawn before everything else: meshes copy the scene's render attributes into
        // their shader combos as they are built, and some of those come from these entities
        var spawned = new Dictionary<PlacedMapNode, BaseEntity>();
        var firstSpawned = entitySystem.Entities.Count;
        var placedEntities = placedNodes
            // The nodes of a path are flattened into the keys of the path
            .Where(static placedNode => placedNode.Node is CMapEntity and not CMapPathNode)
            .OrderByDescending(static placedNode => IsCubemapOrProbe(placedNode.Node));

        var entitiesWithMeshes = placedNodes
            .Where(static placedNode => placedNode.Node is CMapMesh && placedNode.Parent != null)
            .Select(static placedNode => placedNode.Parent!)
            .ToHashSet();

        foreach (var placedNode in placedEntities)
        {
            CancellationToken.ThrowIfCancellationRequested();

            if (SpawnEntity(placedNode, entitiesWithMeshes.Contains(placedNode)) is { } entity)
            {
                spawned[placedNode] = entity;
            }
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
                AddMesh(placedNode, mesh, owner: null, placedOwner: null);
            }
            else if (spawned.TryGetValue(placedNode.Parent, out var owner))
            {
                AddMesh(placedNode, mesh, owner, placedNode.Parent);
            }
        }

        // The render data is only shared between the placements of a mesh while they are being made
        renderData.Clear();

        entitySystem.Activate();

        scene.LightingInfo.StoreLights([.. scene.AllNodes.OfType<SceneLight>()]);

        if (entitySystem.ToolVisuals is { } toolVisuals)
        {
            toolVisuals.AddEntityRelations(scene, [.. entitySystem.Entities.Skip(firstSpawned).Where(entity => entity.Scene == scene && entity.Data != null)]);
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

    private BaseEntity? SpawnEntity(PlacedMapNode placedNode, bool hasMeshes)
    {
        if (placedNode.CreateEntity() is not { } data)
        {
            return null;
        }

        Entities.Add(data);

        // An entity made of meshes is drawn by them, never by the stand-in of its class
        if (hasMeshes && entitySystem.ToolVisuals is HammerEntityVisuals visuals)
        {
            visuals.EntitiesWithGeometry.Add(data);
        }

        try
        {
            return entitySystem.CreateEntity(data, Matrix4x4.Identity, placedNode.WorldLayer ?? EntitiesLayerName, scene, EntityNameFixup.None);
        }
        catch (Exception e) when (e is not OperationCanceledException)
        {
            // A map being edited can name things that are not compiled yet, which must not stop the rest of it
            rendererContext.Logger.LogWarning(e, "Failed to spawn entity '{Classname}' (node {NodeId})",
                data.GetStringProperty("classname"), (placedNode.Node as MapNode)?.NodeID);
            return null;
        }
    }

    private void AddMesh(PlacedMapNode placedNode, CMapMesh mesh, BaseEntity? owner, PlacedMapNode? placedOwner)
    {
        if (!renderData.TryGetValue(mesh, out var meshRenderData))
        {
            meshRenderData = new PolygonMeshRenderData(mesh.Mesh);
            renderData[mesh] = meshRenderData;
        }

        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_mesh_{documentIds[placedNode.Document]}_{mesh.NodeID}");
        var meshToNode = Matrix4x4.Identity;

        // An entity places its nodes at itself, so its meshes are moved into its space. The move is the same
        // for every placement of the entity, since an instance moves the entity and its meshes together.
        if (placedOwner != null && Matrix4x4.Invert(placedOwner.Transform, out var worldToOwner))
        {
            meshToNode = placedNode.Transform * worldToOwner;

            if (!meshToNode.IsIdentity)
            {
                name += "_local";
            }
        }

        var shaderArguments = scene.LightingInfo.CreateShaderArguments();

        // The faces of each material are a mesh of their own, with the vertex streams that material reads
        for (var i = 0; i < meshRenderData.MaterialGroups.Length; i++)
        {
            var group = meshRenderData.MaterialGroups[i];
            var groupName = string.Create(CultureInfo.InvariantCulture, $"{name}_{i}");
            var material = rendererContext.MaterialLoader.GetMaterial(group.Material.Length > 0 ? group.Material : null, shaderArguments);
            var (vbib, bounds) = PolygonMeshRenderData.BuildVertexAndIndexBuffers(group, material.Material.InputSignature, meshToNode);

            var node = new MeshSceneNode(scene, RenderableMesh.CreateMesh(groupName, material, vbib, bounds, rendererContext, sortByMaterial: true))
            {
                Name = groupName,
                LayerName = placedNode.WorldLayer ?? (owner == null ? WorldLayerName : EntitiesLayerName),
                TintAlpha = placedNode.Tint * new Vector4(mesh.TintColor.R, mesh.TintColor.G, mesh.TintColor.B, mesh.RenderAmount) / 255f,
                OverlayRenderOrder = mesh is CMapStaticOverlay overlay ? overlay.RenderOrder : 0,
            };

            if (owner != null)
            {
                owner.AddNode(node);
                continue;
            }

            node.Transform = placedNode.Transform;
            scene.Add(node, dynamic: false);
        }
    }

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

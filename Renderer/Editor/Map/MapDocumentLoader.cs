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
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Utils;
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

    // What the editor baked for each place a node is put, by the name it gives the placement
    private readonly Dictionary<string, CDmeNodeInstanceData> nodeInstanceData = new(StringComparer.Ordinal);
    private bool hasVertexLighting;

    // The faces things collide with, grouped by the collision tags they have. Only kept while the map is loaded.
    private readonly List<CollisionGroup> collisionGroups = [];

    private sealed record CollisionGroup(string[] InteractAs, string[] InteractExclude)
    {
        public List<Vector3> Positions { get; } = [];
        public List<uint> SurfaceProperties { get; } = [];
    }

    // What static overlays are projected onto, with the node each was placed as. Only kept while the map is loaded.
    private readonly List<(PlacedMapNode Placement, OverlayReceiver Receiver)> overlayReceivers = [];

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
    /// Gets whether the map is lit with lighting the editor baked for it, once it is loaded: lightmaps and light
    /// probes, or the lighting of the vertices of its meshes and static props. It is not when nothing was baked, or
    /// the game does not say which version of baked lighting it reads.
    /// </summary>
    public bool HasBakedLighting => scene.LightingInfo.LightmapVersionNumber != 0 || hasVertexLighting;

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

        foreach (var element in document.Root.NodeInstanceData)
        {
            if (element is CDmeNodeInstanceData data)
            {
                nodeInstanceData.TryAdd(data.Name, data);
            }
        }
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
            AddModelVertexLighting(mapEntity);
        }

        LoadingProgress?.Report("Loading map geometry…");

        var overlays = new List<(PlacedMapNode Placement, CMapStaticOverlay Overlay, MapEntity? Owner)>();

        foreach (var placedNode in placedNodes)
        {
            CancellationToken.ThrowIfCancellationRequested();

            MapEntity? owner = null;

            if (placedNode.Node is not CMapMesh mesh || (placedNode.Parent != null && !created.TryGetValue(placedNode.Parent, out owner)))
            {
                continue;
            }

            // Overlays land on everything else, so they go last
            if (mesh is CMapStaticOverlay overlay)
            {
                overlays.Add((placedNode, overlay, owner));
                continue;
            }

            AddMesh(placedNode, mesh, owner);
        }

        if (overlays.Count > 0)
        {
            AddModelOverlayReceivers();

            foreach (var (placedNode, overlay, owner) in overlays)
            {
                CancellationToken.ThrowIfCancellationRequested();

                AddOverlay(placedNode, overlay, owner);
            }
        }

        AddWorldCollision();
        AddModelCollision();

        // The render data is only shared between the placements of a mesh while they are being made
        renderData.Clear();
        overlayReceivers.Clear();
        collisionGroups.Clear();

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

        scene.LightingInfo.ShadowMapper.Allocator = new TimeSlicedShadowAtlasAllocator();

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

        // A placement lit by lighting of its own is drawn from buffers of its own, any other shares them
        var key = vertexLighting != null ? GetPlacementKey(placedNode) : mesh.NodeID.ToString(CultureInfo.InvariantCulture);
        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_mesh_{documentIds[placedNode.Document]}_{key}");
        var shaderArguments = scene.LightingInfo.CreateShaderArguments(hasVertexLighting: vertexLighting != null);
        var mapMesh = AddMapMesh(placedNode, mesh, owner);
        var receiverGroups = new List<PolygonMesh.BakedSubmesh>();
        var receiverBounds = default(AABB?);

        // The faces of each material are a mesh of their own, with the vertex streams that material reads
        for (var i = 0; i < meshRenderData.MaterialGroups.Length; i++)
        {
            var group = meshRenderData.MaterialGroups[i];
            var materialPath = GetMaterialPath(group, owner);
            var material = rendererContext.MaterialLoader.GetMaterial(materialPath, shaderArguments);
            var nodeName = string.Create(CultureInfo.InvariantCulture, $"{name}_{i}");

            // Tools materials show as tool brushes do, and those that block light cast shadows through a mesh
            // that is drawn nowhere else
            if (material.IsToolsMaterial)
            {
                mapMesh.AddNode(AddToolsMaterialNode(nodeName, group, material, placedNode, owner));

                if (material.BlocksLight)
                {
                    mapMesh.AddNode(AddMeshNode($"{nodeName}_shadow", material, meshRenderData.GetBuffers(i, material.Material.InputSignature),
                        placedNode, mesh, owner, placedNode.Transform, castsShadowOnly: true));
                }
            }
            else
            {
                var node = AddMeshNode(nodeName, material, meshRenderData.GetBuffers(i, material.Material.InputSignature), placedNode, mesh, owner, placedNode.Transform);
                mapMesh.AddNode(node);

                // Overlays do not land on faces nothing collides with
                if (material.Material.IntAttributes.GetValueOrDefault("mapbuilder.nonsolid") == 0)
                {
                    receiverGroups.Add(group);
                    receiverBounds = receiverBounds?.Union(node.BoundingBox) ?? node.BoundingBox;
                }
            }

            AddCollision(mesh, owner, group, materialPath, material.Material, placedNode.Transform);
        }

        if (receiverBounds is { } bounds)
        {
            var transform = placedNode.Transform;

            overlayReceivers.Add((placedNode, new OverlayReceiver(OverlayReceiverKind.Mesh, bounds, () =>
            {
                var parts = new List<(Vector3[] Positions, Vector3[] Normals, int[] Indices)>(receiverGroups.Count);

                foreach (var group in receiverGroups)
                {
                    parts.Add((group.Positions, group.Normals, group.Indices));
                }

                return TransformTriangles(parts, transform);
            })));
        }
    }

    private static MapMesh AddMapMesh(PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner)
    {
        var mapMesh = new MapMesh(placedNode, mesh, owner);
        owner?.AddMesh(mapMesh);

        return mapMesh;
    }

    // The class of the entity a mesh is tied to can draw all of it with one material, leaving its faces as they are
    private static string? GetMaterialPath(PolygonMesh.BakedSubmesh group, MapEntity? owner)
        => owner?.EntityClass?.AutoApplyMaterial ?? (group.Material.Length > 0 ? group.Material : null);

    private MeshSceneNode AddMeshNode(string name, RenderMaterial material, (VBIB Buffers, AABB Bounds) geometry, PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner,
        Matrix4x4 transform, bool castsShadowOnly = false)
    {
        var renderableMesh = RenderableMesh.CreateMesh(name, material, geometry.Buffers, geometry.Bounds, rendererContext, sortByMaterial: true);
        MeshSceneNode node = castsShadowOnly
            ? new ShadowCasterSceneNode(scene, renderableMesh) { Name = name }
            : new MeshSceneNode(scene, renderableMesh) { Name = name };

        node.LayerName = GetLayerName(placedNode, owner);
        node.TintAlpha = placedNode.Tint * new Vector4(mesh.TintColor.R, mesh.TintColor.G, mesh.TintColor.B, mesh.RenderAmount) / 255f;
        node.OverlayRenderOrder = mesh is CMapStaticOverlay overlay ? overlay.RenderOrder : 0;
        node.Transform = transform;
        node.EntityData = owner?.KeyValues;

        if (IsShadowless(material))
        {
            node.Flags |= ObjectTypeFlags.NoShadows;
        }

        scene.Add(node, dynamic: false);

        return node;
    }

    private ToolsMaterialSceneNode AddToolsMaterialNode(string name, PolygonMesh.BakedSubmesh group, RenderMaterial material, PlacedMapNode placedNode, MapEntity? owner)
    {
        var node = ToolsMaterialSceneNode.Create(scene, name, group, material.Material.Name);
        node.LayerName = GetLayerName(placedNode, owner);
        node.Transform = placedNode.Transform;
        node.EntityData = owner?.KeyValues;

        scene.Add(node, dynamic: false);

        return node;
    }

    private static string GetLayerName(PlacedMapNode placedNode, MapEntity? owner)
        => placedNode.WorldLayer ?? (owner == null ? WorldLayerName : EntitiesLayerName);

    // An overlay is drawn where it lands, as triangles in the world, lit by what the editor baked at their corners.
    // Each placement lands somewhere else, so it is drawn from buffers of its own.
    private void AddOverlay(PlacedMapNode placedNode, CMapStaticOverlay overlay, MapEntity? owner)
    {
        var groups = StaticOverlayProjector.Project(overlay, placedNode.RigidTransform, GetOverlayReceivers(placedNode, overlay), FindInstanceData(placedNode));
        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_overlay_{documentIds[placedNode.Document]}_{GetPlacementKey(placedNode)}");
        var mapMesh = AddMapMesh(placedNode, overlay, owner);

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var isLit = HasVertexLighting(group);
            var material = rendererContext.MaterialLoader.GetMaterial(GetMaterialPath(group, owner), scene.LightingInfo.CreateShaderArguments(hasVertexLighting: isLit));
            var geometry = PolygonMeshRenderData.BuildVertexAndIndexBuffers(group, material.Material.InputSignature);

            hasVertexLighting |= isLit;

            mapMesh.AddNode(AddMeshNode(string.Create(CultureInfo.InvariantCulture, $"{name}_{i}"), material, geometry, placedNode, overlay, owner, Matrix4x4.Identity));
        }
    }

    // Everything, or only the targets of the overlay and what is under them as it was placed along with the overlay,
    // which for a target that is an instance or a prefab is everything it places
    private List<OverlayReceiver> GetOverlayReceivers(PlacedMapNode placedNode, CMapStaticOverlay overlay)
    {
        var receivers = new List<OverlayReceiver>();

        if ((MapOverlayProjectionMode)overlay.ProjectionMode != MapOverlayProjectionMode.Targets)
        {
            foreach (var (_, receiver) in overlayReceivers)
            {
                receivers.Add(receiver);
            }

            return receivers;
        }

        var targets = new HashSet<int>();
        AddTargets(placedNode.Document.World, isUnderTarget: false);

        var path = placedNode.InstancePath;

        foreach (var (placement, receiver) in overlayReceivers)
        {
            var receiverPath = placement.InstancePath;

            if (!StartsWith(receiverPath, path))
            {
                continue;
            }

            var isTarget = receiverPath.Count > path.Count
                ? targets.Contains(receiverPath[path.Count])
                : placement.Document == placedNode.Document && placement.Node is MapNode { NodeID: var id } && targets.Contains(id);

            if (isTarget)
            {
                receivers.Add(receiver);
            }
        }

        return receivers;

        void AddTargets(MapNode node, bool isUnderTarget)
        {
            var isTarget = isUnderTarget || overlay.ProjectionTargets.Contains(node.NodeID);

            if (isTarget)
            {
                targets.Add(node.NodeID);
            }

            foreach (var child in node.Children)
            {
                if (child is MapNode childNode)
                {
                    AddTargets(childNode, isTarget);
                }
            }
        }

        static bool StartsWith(IReadOnlyList<int> path, IReadOnlyList<int> prefix)
        {
            if (path.Count < prefix.Count)
            {
                return false;
            }

            for (var i = 0; i < prefix.Count; i++)
            {
                if (path[i] != prefix[i])
                {
                    return false;
                }
            }

            return true;
        }
    }

    // Overlays land on the most detailed render meshes of the models of entities, unless the models can animate
    private void AddModelOverlayReceivers()
    {
        var triangles = new Dictionary<Model, List<(Vector3[] Positions, Vector3[] Normals, int[] Indices)>>();

        foreach (var mapEntity in mapEntities)
        {
            foreach (var helper in mapEntity.Helpers)
            {
                if (helper is not ModelHelper { IsEditorModel: false, Model: { } model, ModelNode: { Animations.Count: 0 } modelNode })
                {
                    continue;
                }

                var transform = modelNode.Transform;

                overlayReceivers.Add((mapEntity.PlacedNode, new OverlayReceiver(OverlayReceiverKind.Model, modelNode.BoundingBox, () =>
                {
                    if (!triangles.TryGetValue(model, out var parts))
                    {
                        triangles[model] = parts = GetModelTriangles(model);
                    }

                    return TransformTriangles(parts, transform);
                })));
            }
        }
    }

    private List<(Vector3[] Positions, Vector3[] Normals, int[] Indices)> GetModelTriangles(Model model)
    {
        var meshes = new List<Mesh>();

        foreach (var mesh in model.GetEmbeddedMeshes())
        {
            if ((mesh.LodMask & 1) != 0)
            {
                meshes.Add(mesh.Mesh);
            }
        }

        foreach (var reference in model.GetReferenceMeshNamesAndLoD())
        {
            if ((reference.LodMask & 1) != 0 && rendererContext.FileLoader.LoadFileCompiled(reference.MeshName)?.DataBlock is Mesh mesh)
            {
                meshes.Add(mesh);
            }
        }

        var parts = new List<(Vector3[], Vector3[], int[])>();

        foreach (var mesh in meshes)
        {
            var vbib = mesh.VBIB;

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    var vertexBuffer = vbib.VertexBuffers[drawCall.GetArray("m_vertexBuffers")[0].GetInt32Property("m_hBuffer")];

                    if (FindAttribute(vertexBuffer, "POSITION") is not { } position || FindAttribute(vertexBuffer, "NORMAL") is not { } normal)
                    {
                        continue;
                    }

                    var indexBuffer = vbib.IndexBuffers[drawCall.GetSubCollection("m_indexBuffer").GetInt32Property("m_hBuffer")];
                    var indices = GltfModelExporter.ReadIndices(indexBuffer, drawCall.GetInt32Property("m_nStartIndex"), drawCall.GetInt32Property("m_nIndexCount"), drawCall.GetInt32Property("m_nBaseVertex"));

                    parts.Add((VBIB.GetVector3AttributeArray(vertexBuffer, position), VBIB.GetNormalTangentArray(vertexBuffer, normal).Normals, indices));
                }
            }
        }

        return parts;

        static VBIB.RenderInputLayoutField? FindAttribute(VBIB.OnDiskBufferData vertexBuffer, string semanticName)
        {
            foreach (var field in vertexBuffer.InputLayoutFields)
            {
                if (field.SemanticName == semanticName)
                {
                    return field;
                }
            }

            return null;
        }
    }

    // Triangles moved into the world, with their normals turned along but not rescaled
    private static (Vector3[] Positions, Vector3[] Normals) TransformTriangles(List<(Vector3[] Positions, Vector3[] Normals, int[] Indices)> parts, Matrix4x4 transform)
    {
        var positions = new List<Vector3>();
        var normals = new List<Vector3>();

        foreach (var (partPositions, partNormals, indices) in parts)
        {
            foreach (var index in indices)
            {
                positions.Add(Vector3.Transform(partPositions[index], transform));
                normals.Add(Vector3.TransformNormal(partNormals[index], transform));
            }
        }

        return ([.. positions], [.. normals]);
    }

    private static bool HasVertexLighting(PolygonMesh.BakedSubmesh group)
    {
        foreach (var stream in group.Streams)
        {
            if (stream.Stream.SemanticName == "PerVertexLighting")
            {
                return true;
            }
        }

        return false;
    }

    // The lighting the editor baked for the vertices of a mesh where it is put, as streams that replace the ones of the mesh
    private PolygonMeshStream[]? GetVertexLighting(PlacedMapNode placedNode, CMapMesh mesh)
        => FindInstanceData(placedNode) is { } data ? data.CreateVertexLightingStreams(mesh.Mesh) : null;

    private CDmeNodeInstanceData? FindInstanceData(PlacedMapNode placedNode) => nodeInstanceData.GetValueOrDefault(GetPlacementKey(placedNode));

    // The lighting the editor baked for the vertices of the model of a static prop where it is put. It runs through the
    // draw calls of every mesh of the model in order, and is not used once the model no longer has as many vertices.
    private void AddModelVertexLighting(MapEntity mapEntity)
    {
        if (mapEntity.Classname != "prop_static"
            || FindInstanceData(mapEntity.PlacedNode) is not { VertexLightingData: { Count: > 0 } colors }
            || FindModelNode(mapEntity) is not { } modelNode)
        {
            return;
        }

        var meshes = new List<RenderableMesh>(modelNode.AllRenderableMeshes);
        meshes.Sort(static (a, b) => a.MeshIndex.CompareTo(b.MeshIndex));

        var drawCalls = new List<(RenderableMesh Mesh, DrawCall DrawCall)>();
        var vertexCount = 0L;

        foreach (var mesh in meshes)
        {
            var meshDrawCalls = new List<DrawCall>(mesh.DrawCalls);
            meshDrawCalls.Sort(static (a, b) => a.Index.CompareTo(b.Index));

            foreach (var drawCall in meshDrawCalls)
            {
                drawCalls.Add((mesh, drawCall));
                vertexCount += drawCall.VertexCount;
            }
        }

        if (vertexCount != colors.Count)
        {
            return;
        }

        VBIB.RenderInputLayoutField[] layout = [new() { SemanticName = "COLOR", SemanticIndex = 1, Format = DXGI_FORMAT.R8G8B8A8_UNORM }];
        var vbib = new VBIB { Resource = null! };
        var next = 0;

        // Each draw call has a buffer of its own, which its vertices are read from where they start
        foreach (var (_, drawCall) in drawCalls)
        {
            var bufferVertexCount = drawCall.BaseVertex + (int)drawCall.VertexCount;
            var data = new byte[bufferVertexCount * 4];

            for (var i = drawCall.BaseVertex; i < bufferVertexCount; i++)
            {
                var color = colors[next++];

                data[i * 4] = color.R;
                data[(i * 4) + 1] = color.G;
                data[(i * 4) + 2] = color.B;
                data[(i * 4) + 3] = color.A;
            }

            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)bufferVertexCount,
                ElementSizeInBytes = 4,
                Data = data,
                InputLayoutFields = layout,
            });
        }

        var buffers = rendererContext.MeshBufferCache.CreateVertexIndexBuffers(
            string.Create(CultureInfo.InvariantCulture, $"{modelNode.Name} vertex lighting {GetPlacementKey(mapEntity.PlacedNode)}"), vbib);

        for (var i = 0; i < drawCalls.Count; i++)
        {
            var (mesh, drawCall) = drawCalls[i];

            mesh.SetBakedVertexLighting(drawCall, new VertexDrawBuffer { Handle = buffers.VertexBuffers[i], BufferIndex = i, ElementSizeInBytes = 4, InputLayoutFields = layout });
        }

        hasVertexLighting = true;

        static ModelSceneNode? FindModelNode(MapEntity mapEntity)
        {
            foreach (var helper in mapEntity.Helpers)
            {
                if (helper is ModelHelper modelHelper)
                {
                    return modelHelper.ModelNode;
                }
            }

            return null;
        }
    }

    // Names one placement of a node, the way the editor names what it bakes for it
    private static string GetPlacementKey(PlacedMapNode placedNode)
    {
        var ids = placedNode.NodeIdPath;
        var parts = new string[ids.Count];

        for (var i = 0; i < ids.Count; i++)
        {
            parts[i] = ids[i].ToString(CultureInfo.InvariantCulture);
        }

        return string.Join('.', parts);
    }

    // Faces collide as their material compiles to, unless the entity they belong to is not solid, and in the
    // interaction layers their mesh adds
    private void AddCollision(CMapMesh mesh, MapEntity? owner, PolygonMesh.BakedSubmesh group, string? materialPath, Material material, Matrix4x4 transform)
    {
        if (scene.PhysicsWorld == null || !IsSolidAtStart(owner) || mesh.PhysicsType.Equals("none", StringComparison.OrdinalIgnoreCase))
        {
            return;
        }

        // A material that did not load has no name, and a tools material compiles the same without the game that has it
        var materialCollision = material.Name.Length == 0 && materialPath != null && ToolsMaterials.GetCollision(materialPath) is { } toolsCollision
            ? toolsCollision
            : new MaterialCollision(material);

        if (!materialCollision.IsSolid)
        {
            return;
        }

        var interactAs = new SortedSet<string>(StringComparer.Ordinal);
        var interactExclude = new SortedSet<string>(StringComparer.Ordinal);

        // The layers of a material only count when it gives a collision group
        if (materialCollision.CollisionGroup.Length > 0)
        {
            AddLayers(interactAs, materialCollision.InteractAs);
            AddLayers(interactExclude, materialCollision.InteractExclude);
        }

        AddLayers(interactAs, MaterialCollision.SplitLayers(mesh.PhysicsInteractsAs));
        AddLayers(interactExclude, MaterialCollision.SplitLayers(mesh.PhysicsInteractsExclude));

        var collision = GetCollisionGroup([.. interactAs], [.. interactExclude]);
        var surfaceProperty = materialCollision.SurfaceProperty.Length > 0 ? StringToken.Store(materialCollision.SurfaceProperty) : 0u;

        foreach (var index in group.Indices)
        {
            collision.Positions.Add(Vector3.Transform(group.Positions[index], transform));
        }

        for (var i = 0; i < group.Indices.Length / 3; i++)
        {
            collision.SurfaceProperties.Add(surfaceProperty);
        }
    }

    // Layers are matched ignoring case
    private static void AddLayers(SortedSet<string> layers, IEnumerable<string> added)
    {
        foreach (var layer in added)
        {
            layers.Add(layer.ToLowerInvariant());
        }
    }

    // The group of the faces that have exactly these collision tags, which are sorted
    private CollisionGroup GetCollisionGroup(string[] interactAs, string[] interactExclude)
    {
        foreach (var group in collisionGroups)
        {
            if (group.InteractAs.AsSpan().SequenceEqual(interactAs) && group.InteractExclude.AsSpan().SequenceEqual(interactExclude))
            {
                return group;
            }
        }

        var created = new CollisionGroup(interactAs, interactExclude);
        collisionGroups.Add(created);

        return created;
    }

    // A brush entity that starts disabled is not solid until it is enabled. A func_brush is never solid unless its
    // solidity says otherwise: always (2), or while it is enabled (0).
    private static bool IsSolidAtStart(MapEntity? owner)
    {
        if (owner == null)
        {
            return true;
        }

        var startsEnabled = GetIntegerKey("startdisabled", 0) == 0;

        return owner.Classname == "func_brush"
            ? GetIntegerKey("solidity", 1) switch
            {
                0 => startsEnabled,
                2 => true,
                _ => false,
            }
            : startsEnabled;

        int GetIntegerKey(string key, int defaultValue)
            => EnvironmentKeys.ParseInteger(owner.KeyValues.GetStringProperty(key) ?? string.Empty, defaultValue);
    }

    private void AddWorldCollision()
    {
        if (scene.PhysicsWorld is not { } physics || collisionGroups.Count == 0)
        {
            return;
        }

        var meshes = new List<Rubikon.PhysicsMeshData>(collisionGroups.Count);

        foreach (var group in collisionGroups)
        {
            meshes.Add(Rubikon.CreateMeshFromTriangles(group.InteractAs, group.InteractExclude, group.Positions, group.SurfaceProperties));
        }

        physics.Add(scene, new Rubikon(meshes), Matrix4x4.Identity);
    }

    // Props collide with the physics of their models where they are placed, unless they are set not to be solid
    private void AddModelCollision()
    {
        if (scene.PhysicsWorld is not { } physics)
        {
            return;
        }

        var shapes = new Dictionary<Model, Rubikon?>();

        foreach (var mapEntity in mapEntities)
        {
            if (!mapEntity.Classname.StartsWith("prop_", StringComparison.Ordinal)
                || EnvironmentKeys.ParseInteger(mapEntity.KeyValues.GetStringProperty("solid") ?? string.Empty, 6) == 0)
            {
                continue;
            }

            foreach (var helper in mapEntity.Helpers)
            {
                if (helper is not ModelHelper { IsEditorModel: false, Model: { } model, ModelNode: { } modelNode })
                {
                    continue;
                }

                if (!shapes.TryGetValue(model, out var shape))
                {
                    shapes[model] = shape = EntityCollider.LoadPhysics(model, rendererContext.FileLoader) is { } data ? new Rubikon(data) : null;
                }

                if (shape != null)
                {
                    physics.Add(scene, shape, modelNode.Transform);
                }
            }
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

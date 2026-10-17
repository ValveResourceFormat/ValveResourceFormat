using System.Globalization;
using System.IO;
using System.Linq;
using System.Threading;
using Microsoft.Extensions.Logging;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
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

    // What static overlays are projected onto, with the node each was placed as. Only kept while the map is loaded.
    private readonly List<(PlacedMapNode Placement, OverlayReceiver Receiver)> overlayReceivers = [];

    // The faces things collide with, grouped by the collision tags they have. Only kept while the map is loaded.
    private readonly Dictionary<string, CollisionGroup> collisionGroups = new(StringComparer.Ordinal);

    private sealed record CollisionGroup(string[] InteractAs, string[] InteractExclude)
    {
        public List<Vector3> Positions { get; } = [];
        public List<uint> SurfaceProperties { get; } = [];
    }

    // The collision tag the map builder gives faces whose material has an attribute set. Translucent materials are
    // also tagged as windows.
    private static readonly (string Attribute, string Tag)[] CollisionTagAttributes =
    [
        ("mapbuilder.ladder", "ladder"),
        ("mapbuilder.blocklos", "blocklos"),
        ("mapbuilder.blocksound", "blocksound"),
        ("mapbuilder.passbullets", "passbullets"),
        ("mapbuilder.npcclip", "npcclip"),
        ("mapbuilder.playerclip", "playerclip"),
        ("mapbuilder.sky", "sky"),
        ("mapbuilder.water", "water"),
        ("mapbuilder.teleportclip", "teleportclip"),
        ("mapbuilder.navclip", "navclip"),
        ("translucent", "window"),
    ];

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

        var lights = scene.AllNodes.OfType<SceneLight>().ToList();
        scene.LightingInfo.StoreLights(lights);

        // Lights that are off stay in the scene without brightness
        HasLights = lights.Exists(static light => light.Brightness * light.BrightnessScale > 0f);
    }

    // The editor shows every fragment with the cubemaps and light probe volumes it is in, sampling each one's own
    // textures. They are copied into a cube array and an atlas to the same effect, which the scene then owns.
    private void PackBakedLighting()
    {
        var lighting = scene.LightingInfo;
        var logger = rendererContext.Logger;

        if (EnvironmentMapCache.Create(lighting.EnvMaps, logger) is { } cubemaps)
        {
            lighting.AddOwnedTexture(cubemaps.Texture);
            lighting.CubemapType = CubemapType.CubemapArray;
            lighting.Lightmaps["g_tEnvironmentMap"] = cubemaps.Texture;
        }

        var volumes = lighting.LightProbes.FindAll(static probe => probe.Irradiance != null);

        if (volumes.Count > 0 && LightProbeAtlas.Create(volumes, logger) is { } atlas)
        {
            lighting.AddOwnedTexture(atlas.Irradiance);

            if (atlas.DirectLightShadows is { } shadows)
            {
                lighting.AddOwnedTexture(shadows);
            }

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

        // The first helper of the fog global that gives a fog gives it
        scene.FogInfo.LinearFog = globals.GetValueOrDefault("fog")?.Helpers.OfType<ILinearFogSource>().FirstOrDefault()?.GetFog() ?? LinearFog.None;

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
        var receiverGroups = new List<PolygonMesh.BakedSubmesh>();
        var receiverBounds = default(AABB?);

        // The faces of each material are a mesh of their own, with the vertex streams that material reads
        for (var i = 0; i < meshRenderData.MaterialGroups.Length; i++)
        {
            var group = meshRenderData.MaterialGroups[i];
            var material = GetMaterial(group, shaderArguments);
            var (vbib, vertexBounds) = meshRenderData.GetBuffers(i, material.Material.InputSignature);
            var node = AddMeshNode(string.Create(CultureInfo.InvariantCulture, $"{name}_{i}"), material, vbib, vertexBounds, placedNode, mesh, owner, placedNode.Transform);
            var attributes = material.Material.IntAttributes;

            AddCollision(mesh, owner, group, material.Material, placedNode.Transform);

            // Overlays do not land on tool faces, or on faces nothing collides with
            if (attributes.GetValueOrDefault("tools.toolsmaterial") == 0 && attributes.GetValueOrDefault("mapbuilder.nonsolid") == 0)
            {
                receiverGroups.Add(group);
                receiverBounds = receiverBounds?.Union(node.BoundingBox) ?? node.BoundingBox;
            }
        }

        if (receiverBounds is { } bounds)
        {
            var transform = placedNode.Transform;

            overlayReceivers.Add((placedNode, new OverlayReceiver(OverlayReceiverKind.Mesh, bounds,
                () => TransformTriangles(receiverGroups.Select(static group => (group.Positions, group.Normals, group.Indices)), transform))));
        }
    }

    private RenderMaterial GetMaterial(PolygonMesh.BakedSubmesh group, Dictionary<string, byte> shaderArguments)
        => rendererContext.MaterialLoader.GetMaterial(group.Material.Length > 0 ? group.Material : null, shaderArguments);

    private MeshSceneNode AddMeshNode(string name, RenderMaterial material, VBIB vbib, AABB bounds, PlacedMapNode placedNode, CMapMesh mesh, MapEntity? owner, Matrix4x4 transform)
    {
        var node = new MeshSceneNode(scene, RenderableMesh.CreateMesh(name, material, vbib, bounds, rendererContext, sortByMaterial: true))
        {
            Name = name,
            LayerName = placedNode.WorldLayer ?? (owner == null ? WorldLayerName : EntitiesLayerName),
            TintAlpha = placedNode.Tint * new Vector4(mesh.TintColor.R, mesh.TintColor.G, mesh.TintColor.B, mesh.RenderAmount) / 255f,
            OverlayRenderOrder = mesh is CMapStaticOverlay overlay ? overlay.RenderOrder : 0,
            Transform = transform,
            EntityData = owner?.KeyValues,
        };

        if (IsShadowless(material))
        {
            node.Flags |= ObjectTypeFlags.NoShadows;
        }

        scene.Add(node, dynamic: false);
        owner?.AddMeshNode(node);

        return node;
    }

    // An overlay is drawn where it lands, as triangles in the world, lit by what the editor baked at their corners.
    // Each placement lands somewhere else, so it is drawn from buffers of its own.
    private void AddOverlay(PlacedMapNode placedNode, CMapStaticOverlay overlay, MapEntity? owner)
    {
        var groups = StaticOverlayProjector.Project(overlay, placedNode.RigidTransform, GetOverlayReceivers(placedNode, overlay), FindInstanceData(placedNode));
        var name = string.Create(CultureInfo.InvariantCulture, $"vmap_overlay_{documentIds[placedNode.Document]}_{GetPlacementKey(placedNode)}");

        for (var i = 0; i < groups.Length; i++)
        {
            var group = groups[i];
            var lit = group.Streams.Any(static stream => stream.Stream.SemanticName == "PerVertexLighting");
            var material = GetMaterial(group, scene.LightingInfo.CreateShaderArguments(hasVertexLighting: lit));
            var (vbib, bounds) = PolygonMeshRenderData.BuildVertexAndIndexBuffers(group, material.Material.InputSignature);

            hasVertexLighting |= lit;

            AddMeshNode(string.Create(CultureInfo.InvariantCulture, $"{name}_{i}"), material, vbib, bounds, placedNode, overlay, owner, Matrix4x4.Identity);
        }
    }

    // Everything, or only the targets of the overlay and what is under them as it was placed along with the overlay,
    // which for a target that is an instance or a prefab is everything it places
    private IEnumerable<OverlayReceiver> GetOverlayReceivers(PlacedMapNode placedNode, CMapStaticOverlay overlay)
    {
        if ((MapOverlayProjectionMode)overlay.ProjectionMode != MapOverlayProjectionMode.Targets)
        {
            return overlayReceivers.Select(static receiver => receiver.Receiver);
        }

        var targets = new HashSet<int>();

        foreach (var node in EnumerateNodes(placedNode.Document.World))
        {
            if (overlay.ProjectionTargets.Contains(node.NodeID))
            {
                targets.UnionWith(EnumerateNodes(node).Select(static target => target.NodeID));
            }
        }

        var path = placedNode.InstancePath;

        return overlayReceivers
            .Where(receiver => receiver.Placement.InstancePath is var receiverPath
                && receiverPath.Take(path.Count).SequenceEqual(path)
                && (receiverPath.Count > path.Count
                    ? targets.Contains(receiverPath[path.Count])
                    : receiver.Placement.Document == placedNode.Document && receiver.Placement.Node is MapNode { NodeID: var id } && targets.Contains(id)))
            .Select(static receiver => receiver.Receiver);

        static IEnumerable<MapNode> EnumerateNodes(MapNode node)
            => node.Children.OfType<MapNode>().SelectMany(EnumerateNodes).Prepend(node);
    }

    // Overlays land on the most detailed render meshes of the models of entities, unless the models can animate
    private void AddModelOverlayReceivers()
    {
        var triangles = new Dictionary<Model, List<(Vector3[] Positions, Vector3[] Normals, int[] Indices)>>();

        foreach (var mapEntity in mapEntities)
        {
            foreach (var helper in mapEntity.Helpers.OfType<ModelHelper>())
            {
                if (helper.IsEditorModel || helper.Model is not { } model || helper.ModelNode is not { Animations.Count: 0 } modelNode)
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
        var meshes = model.GetEmbeddedMeshes().Where(static mesh => (mesh.LodMask & 1) != 0).Select(static mesh => mesh.Mesh)
            .Concat(model.GetReferenceMeshNamesAndLoD()
                .Where(static mesh => (mesh.LodMask & 1) != 0)
                .Select(mesh => rendererContext.FileLoader.LoadFileCompiled(mesh.MeshName)?.DataBlock as Mesh)
                .OfType<Mesh>());

        var parts = new List<(Vector3[], Vector3[], int[])>();

        foreach (var mesh in meshes)
        {
            var vbib = mesh.VBIB;

            foreach (var sceneObject in mesh.Data.GetArray("m_sceneObjects"))
            {
                foreach (var drawCall in sceneObject.GetArray("m_drawCalls"))
                {
                    var vertexBuffer = vbib.VertexBuffers[drawCall.GetArray("m_vertexBuffers")[0].GetInt32Property("m_hBuffer")];
                    var position = vertexBuffer.InputLayoutFields.FirstOrDefault(static field => field.SemanticName == "POSITION");
                    var normal = vertexBuffer.InputLayoutFields.FirstOrDefault(static field => field.SemanticName == "NORMAL");

                    if (position.SemanticName == null || normal.SemanticName == null)
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
    }

    // Faces collide unless their material or the entity they belong to is not solid, with the collision tags of their
    // material, their mesh and their entity
    private void AddCollision(CMapMesh mesh, MapEntity? owner, PolygonMesh.BakedSubmesh group, Material material, Matrix4x4 transform)
    {
        var attributes = material.IntAttributes;

        if (!IsSolidAtStart(owner)
            || mesh.PhysicsType.Equals("none", StringComparison.OrdinalIgnoreCase)
            || attributes.GetValueOrDefault("mapbuilder.nonsolid") != 0
            || attributes.GetValueOrDefault("nocollision") != 0)
        {
            return;
        }

        string[] interactAs = [.. CollisionTagAttributes
            .Where(entry => attributes.GetValueOrDefault(entry.Attribute) != 0)
            .Select(static entry => entry.Tag)
            .Concat(SplitCollisionTags(material.StringAttributes.GetValueOrDefault("mapbuilder.collisionproperties")))
            .Concat(SplitCollisionTags(mesh.PhysicsInteractsAs))
            .Concat(SplitCollisionTags(owner?.KeyValues.GetStringProperty("interactas")))
            .Distinct(StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)];
        string[] interactExclude = [.. SplitCollisionTags(mesh.PhysicsInteractsExclude).Distinct(StringComparer.Ordinal).Order(StringComparer.Ordinal)];

        var key = string.Join(',', interactAs) + "|" + string.Join(',', interactExclude);

        if (!collisionGroups.TryGetValue(key, out var collision))
        {
            collisionGroups[key] = collision = new CollisionGroup(interactAs, interactExclude);
        }

        var surfaceProperty = material.StringAttributes.GetValueOrDefault("PhysicsSurfaceProperties") is { Length: > 0 } surface ? StringToken.Store(surface) : 0u;

        foreach (var index in group.Indices)
        {
            collision.Positions.Add(Vector3.Transform(group.Positions[index], transform));
        }

        collision.SurfaceProperties.AddRange(Enumerable.Repeat(surfaceProperty, group.Indices.Length / 3));
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

    private static IEnumerable<string> SplitCollisionTags(string? tags)
        => (tags ?? string.Empty).Split([',', ' ', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).Select(static tag => tag.ToLowerInvariant());

    private void AddWorldCollision()
    {
        if (scene.PhysicsWorld is not { } physics || collisionGroups.Count == 0)
        {
            return;
        }

        var meshes = collisionGroups.Values.Select(static group => Rubikon.CreateMeshFromTriangles(group.InteractAs, group.InteractExclude, group.Positions, group.SurfaceProperties));

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

            foreach (var helper in mapEntity.Helpers.OfType<ModelHelper>())
            {
                if (helper.IsEditorModel || helper.Model is not { } model || helper.ModelNode is not { } modelNode)
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

    // Triangles moved into the world, with their normals turned along but not rescaled
    private static (Vector3[] Positions, Vector3[] Normals) TransformTriangles(IEnumerable<(Vector3[] Positions, Vector3[] Normals, int[] Indices)> parts, Matrix4x4 transform)
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

    // The lighting the editor baked for the vertices of a mesh where it is put, as streams that replace the ones of the mesh
    private PolygonMeshStream[]? GetVertexLighting(PlacedMapNode placedNode, CMapMesh mesh)
        => mesh is not CMapStaticOverlay && FindInstanceData(placedNode) is { } data ? data.CreateVertexLightingStreams(mesh.Mesh) : null;

    private CDmeNodeInstanceData? FindInstanceData(PlacedMapNode placedNode) => nodeInstanceData.GetValueOrDefault(GetPlacementKey(placedNode));

    // Names one placement of a node, the way the editor names what it bakes for it
    private static string GetPlacementKey(PlacedMapNode placedNode)
        => string.Join('.', placedNode.NodeIdPath.Select(static id => id.ToString(CultureInfo.InvariantCulture)));

    // The lighting the editor baked for the vertices of the model of a static prop where it is put. It runs through the
    // draw calls of every mesh of the model in order, and is not used once the model no longer has as many vertices.
    private void AddModelVertexLighting(MapEntity mapEntity)
    {
        if (mapEntity.Classname != "prop_static"
            || FindInstanceData(mapEntity.PlacedNode) is not { VertexLightingData: { Count: > 0 } colors }
            || mapEntity.Helpers.OfType<ModelHelper>().FirstOrDefault()?.ModelNode is not { } modelNode)
        {
            return;
        }

        var drawCalls = modelNode.AllRenderableMeshes
            .OrderBy(static mesh => mesh.MeshIndex)
            .SelectMany(static mesh => mesh.DrawCalls.OrderBy(static drawCall => drawCall.Index).Select(drawCall => (Mesh: mesh, DrawCall: drawCall)))
            .ToList();

        if (drawCalls.Sum(static item => item.DrawCall.VertexCount) != colors.Count)
        {
            return;
        }

        VBIB.RenderInputLayoutField[] layout = [new() { SemanticName = "COLOR", SemanticIndex = 1, Format = DXGI_FORMAT.R8G8B8A8_UNORM }];
        var vbib = new VBIB { Resource = null! };
        var next = 0;

        // Each draw call has a buffer of its own, which its vertices are read from where they start
        foreach (var (_, drawCall) in drawCalls)
        {
            var vertexCount = drawCall.BaseVertex + (int)drawCall.VertexCount;
            var data = new byte[vertexCount * 4];

            for (var i = drawCall.BaseVertex; i < vertexCount; i++)
            {
                var color = colors[next++];

                data[i * 4] = color.R;
                data[(i * 4) + 1] = color.G;
                data[(i * 4) + 2] = color.B;
                data[(i * 4) + 3] = color.A;
            }

            vbib.VertexBuffers.Add(new VBIB.OnDiskBufferData
            {
                ElementCount = (uint)vertexCount,
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

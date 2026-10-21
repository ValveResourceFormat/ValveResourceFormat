using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// An entity of a source map where it was placed. Everything drawn of it comes from the helpers its class
/// declares for the editor of its game, as in Hammer. A gizmo moves it along with the meshes it is made of and the
/// nodes of its path, and writes where it ends up back into the map.
/// </summary>
public sealed class MapEntity : IHelperOwner, IGizmoTransformable
{
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName;
    private readonly IReadOnlyList<IHelperOwner> mapEntities;
    private readonly List<SceneNode> nodes = [];
    private readonly List<MapMesh> meshes = [];
    private readonly Dictionary<int, MapEntity> pathNodes = [];

    private MapPlacement transformStart;
    private Matrix4x4[] nodeTransformsAtStart = [];

    /// <summary>Gets the node of the map the entity is, with where it was placed.</summary>
    public PlacedMapNode PlacedNode { get; }

    /// <inheritdoc/>
    public Scene Scene { get; }

    /// <inheritdoc/>
    public Entity KeyValues { get; }

    /// <inheritdoc/>
    public string Classname { get; }

    /// <inheritdoc/>
    public EntityClass? EntityClass { get; }

    /// <summary>Gets where the entity is placed in the world.</summary>
    public Vector3 Origin { get; private set; }

    /// <summary>Gets how the entity is turned: pitch, yaw and roll in degrees.</summary>
    public Vector3 Angles { get; private set; }

    /// <summary>Gets how the entity is scaled along its own axes.</summary>
    public Vector3 Scales { get; private set; }

    private MapPlacement Placement
    {
        get => new(Origin, Angles, Scales);
        set => (Origin, Angles, Scales) = (value.Origin, value.Angles, value.Scales);
    }

    /// <inheritdoc/>
    public Matrix4x4 Transform => Matrix4x4.CreateScale(Scales) * RigidTransform;

    /// <inheritdoc/>
    public Matrix4x4 RigidTransform => EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin);

    /// <inheritdoc/>
    public string LayerName { get; }

    /// <inheritdoc/>
    /// <remarks>The 3D sky of a source map is a map of its own, which is not loaded with it.</remarks>
    public bool IsInSkybox => false;

    /// <inheritdoc/>
    public AABB? LocalBounds => Helpers.OfType<ModelHelper>().FirstOrDefault()?.ModelBounds;

    /// <inheritdoc/>
    public AABB? ContentBounds
    {
        get
        {
            AABB? bounds = null;

            foreach (var mesh in meshes)
            {
                bounds = bounds?.Union(mesh.Bounds) ?? mesh.Bounds;
            }

            return bounds;
        }
    }

    /// <inheritdoc/>
    public IReadOnlyList<EntityHelper> Helpers { get; private set; } = [];

    /// <summary>Gets the nodes the entity is drawn with.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    /// <inheritdoc/>
    public IHelperOwner? Path { get; }

    internal MapEntity(Scene scene, PlacedMapNode placedNode, Entity keyValues, string layerName, EntityClassTable classes, IReadOnlyList<IHelperOwner> mapEntities, Dictionary<string, List<IHelperOwner>> entitiesByName, MapEntity? path)
    {
        this.mapEntities = mapEntities;
        this.entitiesByName = entitiesByName;

        Scene = scene;
        PlacedNode = placedNode;
        KeyValues = keyValues;
        LayerName = layerName;
        Classname = keyValues.GetStringProperty("classname") ?? string.Empty;
        EntityClass = classes.FindClass(Classname);

        Origin = keyValues.GetVector3Property("origin");
        Angles = keyValues.GetVector3Property("angles");
        Scales = keyValues.GetVector3Property("scales", Vector3.One);

        // A node is found by where it is among the nodes of its path, as the keys the path is flattened into count them
        if (placedNode.Node is CMapPathNode node && path?.PlacedNode.Node is CMapPath pathNode)
        {
            Path = path;
            path.pathNodes[pathNode.Children.OfType<CMapPathNode>().ToList().IndexOf(node)] = this;
        }
    }

    /// <summary>Creates the helpers of the entity.</summary>
    public void CreateHelpers()
    {
        Helpers = EntityHelperFactory.CreateHelpers(this);
    }

    /// <inheritdoc/>
    public void AddNode(SceneNode node, bool followsEntity = false)
    {
        ArgumentNullException.ThrowIfNull(node);

        node.EntityData = KeyValues;
        node.LayerName ??= LayerName;

        nodes.Add(node);
        Scene.Add(node, dynamic: true);
    }

    /// <summary>Adds a mesh the entity is made of, which is part of what it is selected as and moves with it.</summary>
    /// <param name="mesh">The mesh.</param>
    internal void AddMesh(MapMesh mesh) => meshes.Add(mesh);

    /// <inheritdoc/>
    public IReadOnlyList<IHelperOwner> FindByName(string name)
        => entitiesByName.TryGetValue(name, out var entities) ? entities : [];

    /// <inheritdoc/>
    public IHelperOwner? FindByKey(string key, string value)
        => mapEntities.FirstOrDefault(entity => string.Equals(entity.KeyValues.GetStringProperty(key), value, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public IEnumerable<IHelperOwner> FindByClass(string classname)
        => mapEntities.Where(entity => entity.Classname.Equals(classname, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public IHelperOwner? FindPathNode(int index) => pathNodes.GetValueOrDefault(index);

    /// <inheritdoc/>
    /// <remarks>What its helpers cover and the meshes it is made of, as its selection is boxed in the world.</remarks>
    public AABB GetBounds(in Matrix4x4 worldToSpace)
    {
        AABB? bounds = null;

        foreach (var mesh in meshes)
        {
            var meshBounds = mesh.GetBounds(worldToSpace);
            bounds = bounds?.Union(meshBounds) ?? meshBounds;
        }

        foreach (var helper in Helpers)
        {
            if (helper.GetObjectBounds(worldToSpace) is { } helperBounds)
            {
                bounds = bounds?.Union(helperBounds) ?? helperBounds;
            }
        }

        var origin = Vector3.Transform(Origin, worldToSpace);
        return bounds ?? new AABB(origin, origin);
    }

    /// <inheritdoc/>
    /// <remarks>What an instance or prefab places is moved with it, not on its own.</remarks>
    public GizmoTransformModes SupportedTransformModes
    {
        get
        {
            if (PlacedNode.IsInstanced)
            {
                return GizmoTransformModes.None;
            }

            var modes = GizmoTransformModes.All;

            foreach (var helper in Helpers)
            {
                modes &= helper.SupportedTransformModes;
            }

            return modes;
        }
    }

    /// <inheritdoc/>
    IGizmoTransformable? IGizmoTransformable.Parent => Path as MapEntity;

    // The meshes the entity is made of and the nodes of its path, which Hammer moves as its children
    private IEnumerable<IGizmoTransformable> Children => meshes.Cast<IGizmoTransformable>().Concat(pathNodes.Values);

    /// <inheritdoc/>
    public void BeginTransform()
    {
        transformStart = Placement;
        nodeTransformsAtStart = [.. nodes.Select(static node => node.Transform)];

        foreach (var child in Children)
        {
            child.BeginTransform();
        }
    }

    /// <inheritdoc/>
    public void ApplyGizmoTransform(GizmoResult result)
    {
        var kind = EntityClass?.Kind;

        Placement = GizmoTransforms.TransformEntity(transformStart, result, isPath: kind is EntityClassKind.Path or EntityClassKind.PathNode,
            isSolid: kind == EntityClassKind.Solid);
        PlaceNodes();

        foreach (var child in Children)
        {
            child.ApplyGizmoTransform(result);
        }
    }

    /// <inheritdoc/>
    public void EndTransform(bool commit)
    {
        foreach (var child in Children)
        {
            child.EndTransform(commit);
        }

        if (!commit)
        {
            Placement = transformStart;
            PlaceNodes();
            return;
        }

        if (PlacedNode.Node is MapNode node)
        {
            node.Origin = Origin;
            node.Angles = new Datamodel.QAngle(Angles.X, Angles.Y, Angles.Z);
            node.Scales = Scales;
        }

        // The keys the entity spawns with, and those its path flattens its nodes into
        PlacedNode.WritePlacementKeys(KeyValues);

        if (Path is MapEntity path)
        {
            path.PlacedNode.WritePlacementKeys(path.KeyValues);
        }
    }

    // Each node keeps where it is from the entity
    private void PlaceNodes()
    {
        if (!Matrix4x4.Invert(transformStart.Transform, out var fromStart))
        {
            return;
        }

        var toCurrent = fromStart * Transform;

        for (var i = 0; i < nodes.Count && i < nodeTransformsAtStart.Length; i++)
        {
            var transform = nodeTransformsAtStart[i] * toCurrent;

            // A light also lights the scene from where it is
            if (nodes[i] is SceneLight light)
            {
                light.MoveTo(transform);
            }
            else
            {
                nodes[i].Transform = transform;
            }
        }
    }
}

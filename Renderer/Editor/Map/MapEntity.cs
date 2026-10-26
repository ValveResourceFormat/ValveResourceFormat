using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// An entity of a source map where it was placed. Everything drawn of it comes from the helpers its class
/// declares for the editor of its game, as in Hammer.
/// </summary>
public sealed class MapEntity : IHelperOwner
{
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName;
    private readonly IReadOnlyList<IHelperOwner> mapEntities;
    private readonly List<SceneNode> nodes = [];
    private readonly List<MapMesh> meshes = [];
    private readonly Dictionary<int, MapEntity> pathNodes = [];

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
    public Vector3 Origin { get; }

    /// <summary>Gets how the entity is turned: pitch, yaw and roll in degrees.</summary>
    public Vector3 Angles { get; }

    /// <summary>Gets how the entity is scaled along its own axes.</summary>
    public Vector3 Scales { get; }

    /// <inheritdoc/>
    public Matrix4x4 Transform => Matrix4x4.CreateScale(Scales) * RigidTransform;

    /// <inheritdoc/>
    public Matrix4x4 RigidTransform => EntityTransformHelper.ToRigidTransformationMatrix(Angles, Origin);

    /// <inheritdoc/>
    public string LayerName { get; }

    /// <inheritdoc/>
    public AABB? LocalBounds
    {
        get
        {
            foreach (var helper in Helpers)
            {
                if (helper is ModelHelper model)
                {
                    return model.ModelBounds;
                }
            }

            return null;
        }
    }

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
            path.pathNodes[GetPathNodeIndex(pathNode, node)] = this;
        }
    }

    private static int GetPathNodeIndex(CMapPath path, CMapPathNode node)
    {
        var index = 0;

        foreach (var child in path.Children)
        {
            if (child == node)
            {
                return index;
            }

            if (child is CMapPathNode)
            {
                index++;
            }
        }

        return -1;
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

    /// <summary>Adds a mesh the entity is made of, which is part of what it is selected as.</summary>
    /// <param name="mesh">The mesh.</param>
    internal void AddMesh(MapMesh mesh) => meshes.Add(mesh);

    /// <inheritdoc/>
    public IReadOnlyList<IHelperOwner> FindByName(string name)
        => entitiesByName.TryGetValue(name, out var entities) ? entities : [];

    /// <inheritdoc/>
    public IHelperOwner? FindByKey(string key, string value)
    {
        foreach (var entity in mapEntities)
        {
            if (string.Equals(entity.KeyValues.GetStringProperty(key), value, StringComparison.OrdinalIgnoreCase))
            {
                return entity;
            }
        }

        return null;
    }

    /// <inheritdoc/>
    public IEnumerable<IHelperOwner> FindByClass(string classname)
    {
        foreach (var entity in mapEntities)
        {
            if (entity.Classname.Equals(classname, StringComparison.OrdinalIgnoreCase))
            {
                yield return entity;
            }
        }
    }

    /// <inheritdoc/>
    public IHelperOwner? FindPathNode(int index) => pathNodes.GetValueOrDefault(index);
}

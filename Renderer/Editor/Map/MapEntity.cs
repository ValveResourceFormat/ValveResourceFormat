using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.ContentFormats.ValveMap;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// An entity of a source map where it was placed. Everything drawn of it comes from the helpers its class
/// declares for the editor of its game, as in Hammer, and it stays where the map put it.
/// </summary>
public sealed class MapEntity : IHelperOwner
{
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName;
    private readonly IReadOnlyList<IHelperOwner> mapEntities;
    private readonly List<SceneNode> nodes = [];
    private readonly List<SceneNode> meshNodes = [];
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

            foreach (var node in meshNodes)
            {
                bounds = bounds?.Union(node.BoundingBox) ?? node.BoundingBox;
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

    /// <summary>Adds a node of a mesh the entity is made of, which is part of what it is selected as.</summary>
    /// <param name="node">The node, already in the scene.</param>
    internal void AddMeshNode(SceneNode node) => meshNodes.Add(node);

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
}

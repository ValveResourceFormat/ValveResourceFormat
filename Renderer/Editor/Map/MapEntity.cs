using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.EntityLump;

namespace ValveResourceFormat.Renderer.Editor.Map;

/// <summary>
/// An entity of a source map where it was placed. Everything drawn of it comes from the helpers its class
/// declares in the FGD files of its game, as in Hammer, and it stays where the map put it.
/// </summary>
public sealed class MapEntity : IHelperOwner
{
    private readonly Dictionary<string, List<IHelperOwner>> entitiesByName;
    private readonly IReadOnlyList<IHelperOwner> mapEntities;
    private readonly List<SceneNode> nodes = [];

    /// <summary>Gets the node of the map the entity is, with where it was placed.</summary>
    public PlacedMapNode PlacedNode { get; }

    /// <inheritdoc/>
    public Scene Scene { get; }

    /// <inheritdoc/>
    public Entity KeyValues { get; }

    /// <inheritdoc/>
    public string Classname { get; }

    /// <inheritdoc/>
    public FgdClass? FgdClass { get; }

    /// <inheritdoc/>
    public Matrix4x4 Transform { get; }

    /// <inheritdoc/>
    public Matrix4x4 RigidTransform { get; }

    /// <inheritdoc/>
    public string LayerName { get; }

    /// <inheritdoc/>
    public IReadOnlyList<EntityHelper> Helpers { get; private set; } = [];

    /// <summary>Gets the nodes the entity is drawn with.</summary>
    public IReadOnlyList<SceneNode> Nodes => nodes;

    internal MapEntity(Scene scene, PlacedMapNode placedNode, Entity keyValues, string layerName, ForgeGameData fgd, IReadOnlyList<IHelperOwner> mapEntities, Dictionary<string, List<IHelperOwner>> entitiesByName)
    {
        this.mapEntities = mapEntities;
        this.entitiesByName = entitiesByName;

        Scene = scene;
        PlacedNode = placedNode;
        KeyValues = keyValues;
        LayerName = layerName;
        Classname = keyValues.GetStringProperty("classname") ?? string.Empty;
        FgdClass = fgd.FindClass(Classname);

        RigidTransform = EntityTransformHelper.ToRigidTransformationMatrix(keyValues.GetVector3Property("angles"), keyValues.GetVector3Property("origin"));
        Transform = Matrix4x4.CreateScale(keyValues.GetVector3Property("scales", Vector3.One)) * RigidTransform;
    }

    /// <summary>Creates the helpers of the entity.</summary>
    /// <param name="level">How much the map creates.</param>
    public void CreateHelpers(HelperLevel level)
    {
        Helpers = EntityHelperFactory.CreateHelpers(this, level);
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

    /// <inheritdoc/>
    public IReadOnlyList<IHelperOwner> FindByName(string name)
        => entitiesByName.TryGetValue(name, out var entities) ? entities : [];

    /// <inheritdoc/>
    public IHelperOwner? FindByKey(string key, string value)
        => mapEntities.FirstOrDefault(entity => string.Equals(entity.KeyValues.GetStringProperty(key), value, StringComparison.OrdinalIgnoreCase));

    /// <inheritdoc/>
    public IEnumerable<IHelperOwner> FindByClass(string classname)
        => mapEntities.Where(entity => entity.Classname.Equals(classname, StringComparison.OrdinalIgnoreCase));
}

using System.Linq;
using ValveResourceFormat.Renderer.Editor.Helpers;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.Entities;
using ValveResourceFormat.Renderer.World;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Editor.Entities;

/// <summary>
/// An entity of a compiled map as the helpers of its class see it. The game draws what an entity of the
/// game shows, so only the helpers that stand in for an entity in the editor are added to the scene.
/// </summary>
internal sealed class EntityHelperOwner : IHelperOwner
{
    private const string HammerIdKey = "hammeruniqueid";
    private const string PathNodesKey = "pathnodes";
    private const string PathIdKey = "path_uniqueid";
    private const string PathIndexKey = "path_index";

    private readonly BaseEntity entity;
    private readonly HammerEntityVisuals visuals;

    /// <summary>
    /// Gets or sets the nodes that are placed at the entity, while they are held back for the entity to take
    /// one as its own, or <see langword="null"/> when every node is given to the entity as it is added.
    /// </summary>
    public List<SceneNode>? PlacedNodes { get; set; }

    public Scene Scene => entity.Scene;

    public EntityLump.Entity KeyValues { get; }

    public string Classname => entity.Classname;

    public EntityClass? EntityClass { get; }

    public Matrix4x4 Transform => entity.Transform;

    public Matrix4x4 RigidTransform => entity.RigidTransform;

    public string LayerName => entity.LayerName ?? string.Empty;

    /// <summary>Gets or sets the node the helpers stand in for the entity with, which the entity took as its own.</summary>
    public SceneNode? StandIn { get; set; }

    public bool IsInSkybox => entity.Scene.IsSkybox;

    // What the game draws the entity with, unless the helpers stand in for it
    public AABB? LocalBounds => entity.RootNode is { IsPointMarker: false } root ? root.LocalBoundingBox : null;

    public AABB? ContentBounds => entity.RootNode is { IsPointMarker: false } root && root != StandIn ? root.BoundingBox : null;

    public IReadOnlyList<EntityHelper> Helpers { get; private set; } = [];

    public EntityHelperOwner(BaseEntity entity, EntityLump.Entity keyValues, HammerEntityVisuals visuals)
    {
        this.entity = entity;
        this.visuals = visuals;

        KeyValues = keyValues;
        EntityClass = visuals.Classes.FindClass(entity.Classname);
    }

    /// <summary>Creates the helpers of the entity, every one its class declares.</summary>
    public void CreateHelpers()
    {
        Helpers = EntityHelperFactory.CreateHelpers(this);
    }

    public void AddNode(SceneNode node, bool followsEntity = false)
    {
        // The game draws a compiled map, so the shapes only the editor draws are never on the layer of the entity
        if (node is ToolSceneNode && node.LayerName == null)
        {
            node.LayerName = HammerEntityVisuals.MarkerLayerName;
        }

        // What a template spawns stays grouped with it, at the size it has in the map
        if (entity.LayerName == WorldLoader.TemplateLayerName && node.LayerName is HammerEntityVisuals.MarkerLayerName)
        {
            node.LayerName = WorldLoader.TemplateLayerName;
            node.KeepsMarkerSize = false;
        }

        if (followsEntity && PlacedNodes != null)
        {
            PlacedNodes.Add(node);
            return;
        }

        entity.AddNode(node, followsEntity);
    }

    // A 3D sky shares names with the map it is placed in, so only the entities of the same world are found
    public IReadOnlyList<IHelperOwner> FindByName(string name)
        => [.. entity.EntitySystem.FindAllByTargetNameInWorldGroup(name, entity.Scene)
            .Where(static found => found.Data != null)
            .Select(visuals.GetEntity)];

    public IHelperOwner? FindByKey(string key, string value)
        => entity.EntitySystem.Entities
            .FirstOrDefault(found => !found.IsRemoved && found.Data != null && found.Scene.WorldGroup == entity.Scene.WorldGroup
                && string.Equals(found.Data.GetStringProperty(key), value, StringComparison.OrdinalIgnoreCase)) is { } match
            ? visuals.GetEntity(match)
            : null;

    public IEnumerable<IHelperOwner> FindByClass(string classname)
        => entity.EntitySystem.Entities
            .Where(found => !found.IsRemoved && found.Data != null && found.Classname.Equals(classname, StringComparison.OrdinalIgnoreCase))
            .Select(visuals.GetEntity);

    // A node the compiler keeps as an entity of its own names its path by the id of the path, and its place along it
    public IHelperOwner? Path
        => entity.Data?.GetStringProperty(PathIdKey) is { } pathId
            ? FindInLump(found => found.ContainsKey(PathNodesKey) && found.GetStringProperty(HammerIdKey) == pathId)
            : null;

    public IHelperOwner? FindPathNode(int index)
        => entity.Data is { } data && data.ContainsKey(PathNodesKey) && data.GetStringProperty(HammerIdKey) is { } pathId
            ? FindInLump(found => found.GetStringProperty(PathIdKey) == pathId && found.ContainsKey(PathIndexKey) && found.GetInt32Property(PathIndexKey) == index)
            : null;

    // Ids are only unique within the lump of a map or of one of its prefabs
    private EntityHelperOwner? FindInLump(Func<EntityLump.Entity, bool> predicate)
    {
        var found = entity.EntitySystem.Entities.FirstOrDefault(other => !other.IsRemoved && other.Scene == entity.Scene
            && other.Data is { } data && data.ParentLump == entity.Data?.ParentLump && predicate(data));

        return found != null ? visuals.GetEntity(found) : null;
    }
}

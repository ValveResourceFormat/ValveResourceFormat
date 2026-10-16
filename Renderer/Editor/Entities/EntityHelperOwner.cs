using System.Linq;
using ValveResourceFormat.IO.Fgd;
using ValveResourceFormat.Renderer.Editor.Helpers;
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

    public FgdClass? FgdClass { get; }

    public Matrix4x4 Transform => entity.Transform;

    public Matrix4x4 RigidTransform => entity.RigidTransform;

    public string LayerName => entity.LayerName ?? string.Empty;

    public IReadOnlyList<EntityHelper> Helpers { get; private set; } = [];

    public EntityHelperOwner(BaseEntity entity, EntityLump.Entity keyValues, HammerEntityVisuals visuals)
    {
        this.entity = entity;
        this.visuals = visuals;

        KeyValues = keyValues;
        FgdClass = visuals.Fgd.FindClass(entity.Classname);
    }

    /// <summary>
    /// Gets whether a helper stands in for the entity in the editor, rather than drawing what the game
    /// shows of it.
    /// </summary>
    public static bool IsStandIn(EntityHelper helper)
        => helper is AlignedBoxHelper or IconHelper or ModelHelper { IsNamedByClass: true };

    /// <summary>Gets whether a helper only draws shapes of the editor, which the game does not show.</summary>
    public static bool IsToolShape(EntityHelper helper)
        => helper is AnglesHelper or BoxHelper or SphereHelper or VecLineHelper or WireBoxHelper;

    /// <summary>Creates the helpers of the entity, every one its class declares.</summary>
    public void CreateHelpers()
    {
        Helpers = EntityHelperFactory.CreateHelpers(this, HelperLevel.All);
    }

    public void AddNode(SceneNode node, bool followsEntity = false)
    {
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
}

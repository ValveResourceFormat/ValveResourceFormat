using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What a tool draws for entities beyond what the game shows: a stand-in for an entity with nothing of its
/// own to draw, nodes relating entities to each other, and the collision of the world and its entities. The
/// renderer only draws what the game does, and
/// leaves the rest to whoever sets <see cref="EntitySystem.ToolVisuals"/>.
/// </summary>
public interface IEntityToolVisuals
{
    /// <summary>
    /// Gets the resources the stand-in for an entity of a class loads, so a world can load them alongside
    /// its own.
    /// </summary>
    /// <param name="classname">The entity classname.</param>
    /// <returns>The resource names.</returns>
    IEnumerable<string> GetResourcesToPreload(string classname);

    /// <summary>
    /// Builds the stand-in drawn for an entity that has nothing of its own to draw. It is not added to the
    /// scene: the entity owns and places it.
    /// </summary>
    /// <param name="entity">The entity.</param>
    /// <param name="flags">Flags for the node.</param>
    /// <returns>The node, or <see langword="null"/> to draw nothing.</returns>
    SceneNode? CreateStandIn(BaseEntity entity, ObjectTypeFlags flags);

    /// <summary>
    /// Builds what the tool draws of some collision, which the game does not draw, such as the tools materials it was
    /// built from. The nodes are not added to the scene, and are in the space of the collision.
    /// </summary>
    /// <param name="scene">The scene the nodes are for.</param>
    /// <param name="physics">The collision.</param>
    /// <param name="name">What the nodes are named.</param>
    /// <param name="entity">The entity the collision belongs to, or <see langword="null"/> for the collision of the world.</param>
    /// <returns>The nodes.</returns>
    IEnumerable<SceneNode> CreateCollisionVisuals(Scene scene, PhysAggregateData physics, string name, BaseEntity? entity);

    /// <summary>
    /// Builds what the tool draws of one part of some collision, in the part's own space, for a body that moves
    /// with the part alone, such as a ragdoll's. The node is not added to the scene.
    /// </summary>
    /// <param name="scene">The scene the node is for.</param>
    /// <param name="physics">The collision.</param>
    /// <param name="partIndex">The part.</param>
    /// <param name="name">What the node is named.</param>
    /// <returns>The node.</returns>
    SceneNode CreatePartCollisionVisual(Scene scene, PhysAggregateData physics, int partIndex, string? name);

    /// <summary>Adds nodes relating the entities a world spawned into a scene, once all of them have spawned.</summary>
    /// <param name="scene">The scene they spawned into.</param>
    /// <param name="entities">The spawned entities.</param>
    void AddEntityRelations(Scene scene, IReadOnlyList<BaseEntity> entities);
}

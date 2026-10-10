namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// What a tool draws for entities beyond what the game shows: a stand-in for an entity with nothing of its
/// own to draw, and nodes relating entities to each other. The renderer only draws what the game does, and
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

    /// <summary>Adds nodes relating the entities a world spawned into a scene, once all of them have spawned.</summary>
    /// <param name="scene">The scene they spawned into.</param>
    /// <param name="entities">The spawned entities.</param>
    void AddEntityRelations(Scene scene, IReadOnlyList<BaseEntity> entities);
}

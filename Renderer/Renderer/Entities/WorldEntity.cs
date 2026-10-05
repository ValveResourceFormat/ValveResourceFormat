namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Source's <c>CWorld</c>, the root of the entity hierarchy. An entity world has exactly one, created on
/// its own rather than from a map: a map's <c>worldspawn</c> keyvalues never spawn an entity.
/// </summary>
public sealed class WorldEntity : BaseEntity
{
    /// <summary>Initializes the world entity, which has no keyvalues.</summary>
    /// <param name="system">The entity world it is the root of.</param>
    /// <param name="scene">The scene of the map it was created for.</param>
    public WorldEntity(EntitySystem system, Scene scene) : base(system, scene, "worldent")
    {
    }
}

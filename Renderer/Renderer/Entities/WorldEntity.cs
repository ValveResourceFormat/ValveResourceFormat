namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The root of the entity hierarchy, one per entity world. Created directly: the map's
/// <c>worldspawn</c> keyvalues never spawn an entity.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CWorld">CWorld</seealso>
public sealed class WorldEntity : BaseEntity
{
    /// <summary>Initializes the world entity, which has no keyvalues.</summary>
    /// <param name="system">The entity world it is the root of.</param>
    /// <param name="scene">The scene of the map it was created for.</param>
    public WorldEntity(EntitySystem system, Scene scene) : base(system, scene, "worldent")
    {
    }
}

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An entity whose classname the entity system does not implement, and which has no model: a marker such
/// as <c>info_target</c>. It is in the world so that others can find it by name, target it with entity I/O
/// and read where it is, and it is drawn as its editor icon, but it does nothing itself.
/// </summary>
/// <remarks>One with a model is a <see cref="GenericModelEntity"/>.</remarks>
public sealed class GenericEntity : BaseEntity
{
    /// <summary>Initializes an entity from its keyvalues.</summary>
    public GenericEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}

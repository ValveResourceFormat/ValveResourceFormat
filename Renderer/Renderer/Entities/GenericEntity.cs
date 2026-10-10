namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An unimplemented classname with no model, such as <c>info_target</c>. Drawn as its editor icon, it is
/// only a marker that others can find by name, target with entity I/O and read the position of.
/// </summary>
/// <remarks>One with a model is a <see cref="GenericModelEntity"/>.</remarks>
public sealed class GenericEntity : BaseEntity
{
    /// <summary>Initializes an entity from its keyvalues.</summary>
    public GenericEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }
}

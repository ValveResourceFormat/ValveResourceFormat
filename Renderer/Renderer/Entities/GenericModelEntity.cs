namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// An unimplemented classname with a model. Draws the model, but does nothing itself.
/// </summary>
/// <remarks>One without a model is a <see cref="GenericEntity"/>.</remarks>
public sealed class GenericModelEntity : BaseModelEntity
{
    /// <summary>Initializes an entity from its keyvalues.</summary>
    public GenericModelEntity(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    // Whether an unimplemented class is solid is not known, and blocking the player with a trigger or
    // clip brush is worse than walking through a prop
    /// <inheritdoc/>
    protected override bool BuildsCollider => false;
}

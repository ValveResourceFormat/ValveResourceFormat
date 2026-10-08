namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// A volume of water. Nothing collides with it; <see cref="EntitySystem.TraceWaterSurface"/> finds where
/// something enters it.
/// </summary>
public sealed class FuncWater : BaseModelEntity
{
    /// <summary>Initializes an entity from its keyvalues.</summary>
    public FuncWater(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
        IsSolid = false;
    }
}

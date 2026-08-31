namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_auto</c>. Fires its outputs when the map starts - how a map kicks off its own wiring.
/// </summary>
public sealed class LogicAuto : BaseEntity
{
    /// <summary>What a <c>logic_auto</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Removes itself after firing.</summary>
        RemoveOnFire = 1,
    }

    /// <summary>Initializes a <c>logic_auto</c> from its keyvalues.</summary>
    public LogicAuto(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
    }

    /// <inheritdoc/>
    public override void Think()
    {
        EntitySystem.TriggerOutput(this, "OnMapSpawn");
        EntitySystem.TriggerOutput(this, "OnNewGame");
        EntitySystem.TriggerOutput(this, "OnMultiNewMap");

        if (HasSpawnFlags(SpawnFlag.RemoveOnFire))
        {
            EntitySystem.Remove(this);
        }
    }

    /// <inheritdoc/>
    public override void RoundStart()
    {
        EntitySystem.TriggerOutput(this, "OnMultiNewRound", EntitySystem.Player);
    }
}

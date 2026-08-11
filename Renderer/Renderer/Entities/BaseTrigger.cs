namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The base of the trigger volumes, Source's <c>CBaseTrigger</c>. A trigger is a brush you pass through
/// that reports what is inside it, so every trigger shares the same setup and the same pair of outputs;
/// only what it does on a touch differs.
/// </summary>
/// <remarks>
/// <see cref="InitTrigger"/> is the shared half of <c>Spawn</c>, exactly as it is in the engine: a
/// trigger's own <c>Spawn</c> calls it rather than repeating the model and solidity setup. Unlike the
/// engine the volume is left visible, because a viewer showing a map's triggers is the point; they draw
/// with the tools materials, so the tools-material toggle still hides them.
/// </remarks>
public abstract class BaseTrigger : BaseModelEntity
{
    /// <summary>Who a trigger reacts to, from its <c>spawnflags</c>.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {

        /// <summary>Players may touch this trigger.</summary>
        AllowClients = 1,

        /// <summary>NPCs may touch this trigger. Nothing here is an NPC yet.</summary>
        AllowNpcs = 2,

        /// <summary>Pushable props may touch this trigger. Nothing here is pushable yet.</summary>
        AllowPushables = 4,

        /// <summary>Physics props may touch this trigger. Nothing here is a physics prop yet.</summary>
        AllowPhysics = 8,

        /// <summary>Everything may touch this trigger.</summary>
        AllowAll = 64,
    }

    /// <summary>
    /// Initializes a trigger from its keyvalues.
    /// </summary>
    /// <param name="system">The world this entity belongs to.</param>
    /// <param name="spawnInfo">The entity's keyvalues and spawn context.</param>
    protected BaseTrigger(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>
    /// The setup every trigger shares: take the brush volume from the authored model, and stand aside from
    /// movement so things pass through instead of colliding. Source's <c>InitTrigger</c>.
    /// </summary>
    protected void InitTrigger()
    {
        IsSolid = false;
        IsTrigger = true;
    }

    /// <summary>
    /// Whether <paramref name="other"/> is the kind of thing this trigger reacts to, from its spawnflags.
    /// Source's <c>PassesTriggerFilters</c>, without the <c>filtername</c> entity filters.
    /// </summary>
    /// <remarks>
    /// A trigger reacts only to what its spawnflags name, as the engine's does: one that names nothing it
    /// accepts never fires. Of the flags, only "everything" and "clients" can be satisfied here, the
    /// player being the one thing in the world that can enter a volume; a trigger that admits only NPCs,
    /// pushables or physics props therefore stays shut. The <c>filtername</c> entity filters are not
    /// consulted at all, so a trigger that passes its flags is not further narrowed by its filter.
    /// </remarks>
    /// <param name="other">The entity inside the volume.</param>
    /// <returns><see langword="true"/> when the touch should register.</returns>
    protected override bool AcceptsTouchFrom(BaseEntity other)
    {
        if (HasSpawnFlags(SpawnFlag.AllowAll))
        {
            return true;
        }

        return other is PlayerEntity && HasSpawnFlags(SpawnFlag.AllowClients);
    }

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        EntitySystem.TriggerOutput(this, "OnStartTouch", other);

        // The first thing to get through the filters, which the touching set has already taken by the
        // time this runs. The counterpart of OnEndTouchAll, and what a map wires to mean "occupied"
        if (TouchingEntities.Count == 1)
        {
            EntitySystem.TriggerOutput(this, "OnStartTouchAll", other);
        }
    }

    /// <inheritdoc/>
    protected override void OnEndTouch(BaseEntity other)
    {
        EntitySystem.TriggerOutput(this, "OnEndTouch", other);

        if (TouchingEntities.Count == 0)
        {
            EntitySystem.TriggerOutput(this, "OnEndTouchAll", other);
        }
    }
}

using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// The base of the trigger volumes, Source's <c>CBaseTrigger</c>. A trigger is a brush you pass through
/// that reports what is inside it, so every trigger shares the same setup and the same set of outputs;
/// only what it does on a touch differs.
/// </summary>
/// <remarks>
/// <see cref="InitTrigger"/> is the shared half of <c>Spawn</c>, as in the engine: a trigger's own
/// <c>Spawn</c> calls it instead of repeating the model and solidity setup. Unlike the engine, the volume
/// stays visible, since showing a map's triggers is the point. They use tools materials, so the
/// tools-material toggle still hides them.
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

        /// <summary>Only players in a vehicle may touch this trigger. No player here drives one.</summary>
        OnlyClientsInVehicles = 32,

        /// <summary>Everything may touch this trigger.</summary>
        AllowAll = 64,

        /// <summary>Only players out of a vehicle may touch this trigger.</summary>
        OnlyClientsOutOfVehicles = 512,
    }

    // What has passed the filters and not yet left, Source's m_hTouchingEntities. Kept apart from the touch
    // links, which also open for things the filters refuse, and which the StartTouch and EndTouch inputs
    // bypass entirely.
    private readonly List<BaseEntity> touchingEntities = [];
    private bool hasVolume = true;

    /// <summary>Gets whether the trigger reacts to anything.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>
    /// Initializes a trigger from its keyvalues.
    /// </summary>
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
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    /// <summary>
    /// Takes the volume out of the world for good while leaving the trigger enabled, so everything inside
    /// stops touching it on the next tick. What a spent <c>trigger_once</c> does in the moment before it is
    /// removed.
    /// </summary>
    protected void RemoveVolume() => hasVolume = false;

    /// <summary>Switches the trigger on; anything already inside is touched on the next tick.</summary>
    [EntityInput("Enable")] protected void InputEnable(EntityInputData data) => IsEnabled = true;

    /// <summary>Switches the trigger off; anything inside stops touching it on the next tick.</summary>
    [EntityInput("Disable")] protected void InputDisable(EntityInputData data) => IsEnabled = false;

    /// <summary>Switches the trigger on if it is off, and off if it is on.</summary>
    [EntityInput("Toggle")] protected void InputToggle(EntityInputData data) => IsEnabled = !IsEnabled;

    /// <summary>
    /// Reports whether anything is inside: <c>OnTouching</c> and then <c>OnTouchingEachEntity</c> once per
    /// entity when something is, <c>OnNotTouching</c> when nothing is.
    /// </summary>
    [EntityInput("TouchTest")]
    protected void InputTouchTest(EntityInputData data)
    {
        if (!IsEnabled)
        {
            return;
        }

        if (touchingEntities.Count == 0)
        {
            EntitySystem.TriggerOutput(this, "OnNotTouching", this);
            return;
        }

        EntitySystem.TriggerOutput(this, "OnTouching", this);

        foreach (var entity in touchingEntities)
        {
            EntitySystem.TriggerOutput(this, "OnTouchingEachEntity", entity.IsRemoved ? null : entity);
        }
    }

    /// <summary>Behaves as if the entity that sent this input had just entered the volume.</summary>
    [EntityInput("StartTouch")]
    protected void InputStartTouch(EntityInputData data)
    {
        if (data.Caller != null)
        {
            OnStartTouch(data.Caller);
        }
    }

    /// <summary>Behaves as if the entity that sent this input had just left the volume.</summary>
    [EntityInput("EndTouch")]
    protected void InputEndTouch(EntityInputData data)
    {
        if (data.Caller != null)
        {
            OnEndTouch(data.Caller);
        }
    }

    /// <summary>
    /// Whether <paramref name="other"/> is the kind of thing this trigger reacts to, from its spawnflags.
    /// Source's <c>PassesTriggerFilters</c>, without the <c>filtername</c> entity filters.
    /// </summary>
    /// <remarks>
    /// A trigger reacts only to what its spawnflags name, like the engine's, so one that names nothing it
    /// accepts never fires. The player is the only thing here that can enter a volume, so only
    /// "everything" and "clients" can pass; a trigger for NPCs, pushables or physics props stays shut.
    /// <c>filtername</c> filters are not read, so passing the flags is enough.
    /// </remarks>
    /// <returns><see langword="true"/> when the touch should register.</returns>
    protected bool PassesTriggerFilters(BaseEntity other)
    {
        var isPlayer = other is PlayerEntity;

        if (!HasSpawnFlags(SpawnFlag.AllowAll) && !(isPlayer && HasSpawnFlags(SpawnFlag.AllowClients)))
        {
            return false;
        }

        // Checked even when everything is allowed
        return !(isPlayer && HasSpawnFlags(SpawnFlag.OnlyClientsInVehicles));
    }

    /// <summary>
    /// A disabled or spent trigger has no volume to be inside, so no touch opens. The filters are not
    /// applied here: things they refuse still touch, and <see cref="OnStartTouch"/> ignores them.
    /// </summary>
    protected override bool AcceptsTouchFrom(BaseEntity other) => IsEnabled && hasVolume;

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        if (!PassesTriggerFilters(other))
        {
            return;
        }

        // The first thing through the filters, and what a map wires to mean "occupied". It goes out
        // ahead of OnStartTouch, as the engine fires it.
        if (!touchingEntities.Contains(other))
        {
            touchingEntities.Add(other);

            if (touchingEntities.Count == 1)
            {
                EntitySystem.TriggerOutput(this, "OnStartTouchAll", other);
            }
        }

        EntitySystem.TriggerOutput(this, "OnStartTouch", other);
        EntitySystem.TriggerOutput(this, "OnTouchingChanged", other);
    }

    /// <inheritdoc/>
    protected override void OnEndTouch(BaseEntity other)
    {
        if (!touchingEntities.Remove(other))
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnEndTouch", other);
        EntitySystem.TriggerOutput(this, "OnTouchingChanged", other);

        // Anything removed while inside never left, so it must not hold the trigger occupied
        touchingEntities.RemoveAll(static entity => entity.IsRemoved);

        if (touchingEntities.Count == 0)
        {
            EntitySystem.TriggerOutput(this, "OnEndTouchAll", other);
        }
    }
}

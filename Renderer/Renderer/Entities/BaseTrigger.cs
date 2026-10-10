using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// Base of the trigger volumes: a brush that reports what is inside it. Subclasses add what a touch does.
/// </summary>
/// <remarks>
/// A trigger's <c>Spawn</c> calls <see cref="InitTrigger"/>. Deliberately stays visible: showing a map's
/// triggers is the point. They use tools materials, so the tools-material toggle still hides them.
/// </remarks>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CBaseTrigger">CBaseTrigger</seealso>
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

    // Passed the filters and not yet left. Touch links also open for filtered-out entities, and the
    // StartTouch and EndTouch inputs bypass the links.
    private readonly List<BaseEntity> touchingEntities = [];
    private bool hasVolume = true;

    /// <summary>Gets whether the trigger reacts to anything.</summary>
    public bool IsEnabled { get; private set; } = true;

    /// <summary>Initializes a trigger from its keyvalues.</summary>
    protected BaseTrigger(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <summary>
    /// Shared trigger setup: non-solid so things pass through, enabled unless <c>startdisabled</c>.
    /// </summary>
    protected void InitTrigger()
    {
        IsSolid = false;
        IsTrigger = true;
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    /// <summary>
    /// Removes the volume for good while the trigger stays enabled; everything inside stops touching it on
    /// the next tick. Used by a spent <c>trigger_once</c> before it is removed.
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
    /// Whether <paramref name="other"/> matches this trigger's spawnflags. <c>filtername</c> filters are not read.
    /// </summary>
    /// <remarks>
    /// The player is the only thing here that can enter a volume, so only "everything" and "clients" can
    /// pass; a trigger for NPCs, pushables or physics props, or one naming nothing, never fires.
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
    /// A disabled or spent trigger accepts no touches. Filters are not applied here;
    /// <see cref="OnStartTouch"/> ignores what they refuse.
    /// </summary>
    protected override bool AcceptsTouchFrom(BaseEntity other) => IsEnabled && hasVolume;

    /// <inheritdoc/>
    protected override void OnStartTouch(BaseEntity other)
    {
        if (!PassesTriggerFilters(other))
        {
            return;
        }

        // The first entity through the filters fires OnStartTouchAll ("occupied") ahead of OnStartTouch
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

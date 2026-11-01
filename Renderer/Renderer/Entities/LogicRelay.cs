using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_relay</c>. Lets one trigger drive many outputs, and a map switch a branch of its wiring
/// on and off from one entity.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/server/CLogicRelay">CLogicRelay</seealso>
public sealed class LogicRelay : BaseEntity
{
    /// <summary>Gets whether the relay passes triggers on.</summary>
    public bool IsEnabled { get; private set; } = true;

    private bool triggerOnce;
    private bool fastRetrigger;
    private bool passthroughCaller;
    private bool isWaitingForRefire;

    /// <summary>Initializes a <c>logic_relay</c> from its keyvalues.</summary>
    public LogicRelay(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        base.Spawn();

        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");

        triggerOnce = KeyValues.GetBooleanProperty("triggeronce");
        fastRetrigger = KeyValues.GetBooleanProperty("fastretrigger");
        passthroughCaller = KeyValues.GetBooleanProperty("passthroughcaller");
    }

    /// <inheritdoc/>
    public override void Think()
    {
        EntitySystem.TriggerOutput(this, "OnSpawn", this);

        if (triggerOnce)
        {
            EntitySystem.Remove(this);
        }
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        // No think without a listener, so a trigger-once relay is not removed before it was ever triggered
        if (EntitySystem.GetLiveConnections(this, "OnSpawn").Any())
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
    }

    [EntityInput("Trigger")]
    private void InputTrigger(EntityInputData data)
    {
        if (!IsEnabled || isWaitingForRefire)
        {
            return;
        }

        var caller = passthroughCaller ? data.Caller : this;

        EntitySystem.TriggerOutput(this, "OnTrigger", data.Activator, caller: caller);

        if (triggerOnce)
        {
            EntitySystem.Remove(this);
            return;
        }

        if (fastRetrigger)
        {
            return;
        }

        // Deaf until the slowest OnTrigger connection has been delivered, so a retrigger cannot overlap it.
        // A connection whose count this trigger used up is no longer counted.
        isWaitingForRefire = true;
        EntitySystem.QueueInput(this, "EnableRefire", activator: this, caller: caller,
            delay: GetLongestDelay("OnTrigger") + 0.001f);
    }

    [EntityInput("EnableRefire")] private void InputEnableRefire(EntityInputData data) => isWaitingForRefire = false;

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => IsEnabled = false;

    [EntityInput("Toggle")] private void InputToggle(EntityInputData data) => IsEnabled = !IsEnabled;

    [EntityInput("CancelPending")]
    private void InputCancelPending(EntityInputData data)
    {
        EntitySystem.CancelQueuedInputsFrom(this);

        // The EnableRefire that would have cleared it may just have been cancelled
        isWaitingForRefire = false;
    }

    private float GetLongestDelay(string outputName)
    {
        var longest = 0f;

        foreach (var connection in EntitySystem.GetLiveConnections(this, outputName))
        {
            longest = MathF.Max(longest, connection.Delay);
        }

        return longest;
    }
}

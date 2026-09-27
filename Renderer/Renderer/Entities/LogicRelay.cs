using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_relay</c>. A named place to send an output so that one trigger can drive many things, and so
/// that a map can switch a whole branch of its wiring on and off from one entity.
/// </summary>
public sealed class LogicRelay : BaseEntity
{
    /// <summary>
    /// What a <c>logic_relay</c>'s <c>spawnflags</c> mean, for maps that predate the <c>TriggerOnce</c> and
    /// <c>FastRetrigger</c> keyvalues.
    /// </summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Fires once and then removes itself.</summary>
        OnlyOnce = 1,

        /// <summary>May be triggered again while a previous trigger is still waiting on its delay.</summary>
        AllowFastRetrigger = 2,
    }

    /// <summary>Gets whether the relay passes anything on. The <c>Disable</c> input clears it.</summary>
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
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");

        triggerOnce = KeyValues.ContainsKey("triggeronce")
            ? KeyValues.GetBooleanProperty("triggeronce")
            : HasSpawnFlags(SpawnFlag.OnlyOnce);
        fastRetrigger = KeyValues.ContainsKey("fastretrigger")
            ? KeyValues.GetBooleanProperty("fastretrigger")
            : HasSpawnFlags(SpawnFlag.AllowFastRetrigger);
        passthroughCaller = KeyValues.GetBooleanProperty("passthroughcaller");

        // No think without a listener, so a trigger-once relay is not removed before it was ever triggered
        if (HasConnections("OnSpawn"))
        {
            SetNextThink(EntitySystem.CurrentTime + EntitySystem.TickInterval);
        }
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

        // Deaf until the slowest OnTrigger connection has been delivered, so a retrigger cannot overlap it
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

    private bool HasConnections(string outputName)
        => Data?.Connections?.Exists(connection => connection.OutputName.Equals(outputName, StringComparison.OrdinalIgnoreCase)) == true;

    private float GetLongestDelay(string outputName)
    {
        var longest = 0f;

        if (Data?.Connections == null)
        {
            return longest;
        }

        foreach (var connection in Data.Connections)
        {
            if (connection.OutputName.Equals(outputName, StringComparison.OrdinalIgnoreCase))
            {
                longest = MathF.Max(longest, connection.Delay);
            }
        }

        return longest;
    }
}

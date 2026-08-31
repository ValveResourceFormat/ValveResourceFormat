using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_relay</c>. A named place to send an output so that one trigger can drive many things, and so
/// that a map can switch a whole branch of its wiring on and off from one entity.
/// </summary>
public sealed class LogicRelay : BaseEntity
{
    /// <summary>What a <c>logic_relay</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Fires once and then switches itself off for good.</summary>
        OnlyOnce = 1,

        /// <summary>May be triggered again while a previous trigger is still waiting on its delay.</summary>
        AllowFastRetrigger = 2,
    }

    /// <summary>Gets whether the relay passes anything on. The <c>Disable</c> input clears it.</summary>
    public bool IsEnabled { get; private set; } = true;

    private bool isWaiting;

    /// <summary>Initializes a <c>logic_relay</c> from its keyvalues.</summary>
    public LogicRelay(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    [EntityInput("Trigger")]
    private void InputTrigger(EntityInputData data)
    {
        if (!IsEnabled || (isWaiting && !HasSpawnFlags(SpawnFlag.AllowFastRetrigger)))
        {
            return;
        }

        // Held until the outputs have been queued, so a relay wired back into itself cannot recurse
        isWaiting = true;

        EntitySystem.TriggerOutput(this, "OnTrigger", data.Activator);

        isWaiting = false;

        if (HasSpawnFlags(SpawnFlag.OnlyOnce))
        {
            IsEnabled = false;
        }
    }

    [EntityInput("Enable")] private void InputEnable(EntityInputData data) => IsEnabled = true;

    [EntityInput("Disable")] private void InputDisable(EntityInputData data) => IsEnabled = false;

    [EntityInput("Toggle")] private void InputToggle(EntityInputData data) => IsEnabled = !IsEnabled;

    [EntityInput("CancelPending")] private void InputCancelPending(EntityInputData data) => EntitySystem.CancelQueuedInputsFrom(this);
}

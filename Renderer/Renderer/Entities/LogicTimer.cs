using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_timer</c>. Fires <c>OnTimer</c> on a repeating interval, either fixed or drawn from a range.
/// </summary>
public sealed class LogicTimer : BaseEntity
{
    /// <summary>Gets the interval between firings in seconds, when the timer is not randomised.</summary>
    public float RefireTime { get; private set; }

    /// <summary>Gets whether the timer is running.</summary>
    public bool IsEnabled { get; private set; }

    private bool useRandomTime;
    private bool pauseAfterFiring;
    private float initialDelay;
    private float lowerBound;
    private float upperBound;

    /// <summary>Initializes a <c>logic_timer</c> from its keyvalues.</summary>
    public LogicTimer(EntitySystem system, EntitySpawnInfo spawnInfo) : base(system, spawnInfo)
    {
    }

    /// <inheritdoc/>
    public override void Spawn()
    {
        useRandomTime = KeyValues.GetBooleanProperty("userandomtime");
        pauseAfterFiring = KeyValues.GetBooleanProperty("pauseafterfiring");
        initialDelay = KeyValues.GetFloatProperty("initialdelay");
        lowerBound = KeyValues.GetFloatProperty("lowerrandombound");
        upperBound = KeyValues.GetFloatProperty("upperrandombound");
        RefireTime = KeyValues.GetFloatProperty("refiretime");

        IsEnabled = !KeyValues.GetBooleanProperty("startdisabled");
    }

    /// <inheritdoc/>
    public override void Activate()
    {
        if (IsEnabled)
        {
            ScheduleNext();
        }
    }

    /// <summary>Fires the timer and schedules the next one.</summary>
    public override void Think()
    {
        if (!IsEnabled)
        {
            return;
        }

        EntitySystem.TriggerOutput(this, "OnTimer");

        // A one-shot: it has done its job and waits to be switched on again rather than coming round
        if (pauseAfterFiring)
        {
            IsEnabled = false;
            return;
        }

        ScheduleNext();
    }

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data)
    {
        if (IsEnabled)
        {
            return;
        }

        IsEnabled = true;
        ScheduleNext(initialDelay);
    }

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data)
    {
        IsEnabled = false;
        SetNextThink(-1f);
    }

    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data)
    {
        if (IsEnabled)
        {
            InputDisable(data);
        }
        else
        {
            InputEnable(data);
        }
    }

    [EntityInput("RefireTime")]
    private void InputRefireTime(EntityInputData data) => RefireTime = data.Float(RefireTime);

    [EntityInput("ResetTimer")]
    private void InputResetTimer(EntityInputData data)
    {
        if (IsEnabled)
        {
            ScheduleNext();
        }
    }

    [EntityInput("FireTimer")]
    private void InputFireTimer(EntityInputData data)
    {
        EntitySystem.TriggerOutput(this, "OnTimer", data.Activator);

        if (IsEnabled)
        {
            ScheduleNext();
        }
    }

    private void ScheduleNext(float extraDelay = 0f)
    {
        var interval = extraDelay + (useRandomTime
            ? lowerBound + (Random.Shared.NextSingle() * MathF.Max(upperBound - lowerBound, 0f))
            : RefireTime);

        // A timer with no interval would fire every tick forever, which is never what a map means
        if (interval <= 0f)
        {
            IsEnabled = false;
            SetNextThink(-1f);
            return;
        }

        SetNextThink(EntitySystem.CurrentTime + interval);
    }
}

using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Entities;

/// <summary>
/// <c>logic_timer</c>. Fires <c>OnTimer</c> on a repeating interval, either fixed or drawn from a range.
/// As an oscillator it alternates <c>OnTimerLow</c> and <c>OnTimerHigh</c> instead.
/// </summary>
public sealed class LogicTimer : BaseEntity
{
    /// <summary>What a <c>logic_timer</c>'s <c>spawnflags</c> mean.</summary>
    [Flags]
    public enum SpawnFlag : uint
    {
        /// <summary>Alternates between <c>OnTimerLow</c> and <c>OnTimerHigh</c> rather than firing <c>OnTimer</c>.</summary>
        Oscillator = 1,
    }

    private const float MinimumRefireTime = 0.01f;

    /// <summary>
    /// Gets the interval between firings in seconds. A randomised timer draws a new one each time it
    /// restarts and keeps it here.
    /// </summary>
    public float RefireTime { get; private set; }

    /// <summary>Gets whether the timer is running.</summary>
    public bool IsEnabled { get; private set; }

    /// <summary>Gets whether the timer is paused, holding <see cref="RemainingTime"/> until it is unpaused.</summary>
    public bool IsPaused { get; private set; }

    /// <summary>Gets the seconds left before the timer fires, as of the last time it was scheduled or paused.</summary>
    public float RemainingTime { get; private set; }

    private bool useRandomTime;
    private bool pauseAfterFiring;
    private bool upDownState;
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
        IsPaused = false;

        // A negative initial delay may pull the first firing earlier, but never before the timer started
        if (useRandomTime)
        {
            lowerBound = MathF.Abs(lowerBound);
            upperBound = MathF.Abs(upperBound);

            if (lowerBound > upperBound)
            {
                (lowerBound, upperBound) = (upperBound, lowerBound);
            }

            initialDelay = MathF.Max(initialDelay, -lowerBound);
        }
        else
        {
            RefireTime = MathF.Max(RefireTime, MinimumRefireTime);
            initialDelay = MathF.Max(initialDelay, -RefireTime);
        }

        if (!IsEnabled)
        {
            Disable();
            return;
        }

        RestartTimer();
    }

    /// <summary>Fires the timer and schedules the next one.</summary>
    public override void Think()
    {
        if (!IsEnabled)
        {
            return;
        }

        // The initial delay only ever postpones the first firing
        initialDelay = 0f;

        if (HasSpawnFlags(SpawnFlag.Oscillator))
        {
            EntitySystem.TriggerOutput(this, upDownState ? "OnTimerHigh" : "OnTimerLow", this);
            upDownState = !upDownState;
        }
        else
        {
            EntitySystem.TriggerOutput(this, "OnTimer", this);
        }

        RestartTimer();

        if (pauseAfterFiring)
        {
            Pause();
        }
    }

    [EntityInput("Enable")]
    private void InputEnable(EntityInputData data) => Enable();

    [EntityInput("Disable")]
    private void InputDisable(EntityInputData data) => Disable();

    [EntityInput("Toggle")]
    private void InputToggle(EntityInputData data)
    {
        if (IsEnabled)
        {
            Disable();
        }
        else
        {
            Enable();
        }
    }

    [EntityInput("RefireTime")]
    private void InputRefireTime(EntityInputData data)
    {
        var refireTime = MathF.Max(MinimumRefireTime, data.Float());

        if (RefireTime != refireTime)
        {
            RefireTime = refireTime;
            RestartTimer();
        }
    }

    [EntityInput("ResetTimer")]
    private void InputResetTimer(EntityInputData data) => RestartTimer();

    [EntityInput("FireTimer")]
    private void InputFireTimer(EntityInputData data) => Think();

    [EntityInput("AddToTimer")]
    private void InputAddToTimer(EntityInputData data) => ShiftTimer(data.Float());

    [EntityInput("SubtractFromTimer")]
    private void InputSubtractFromTimer(EntityInputData data) => ShiftTimer(-data.Float());

    [EntityInput("PauseTimer")]
    private void InputPauseTimer(EntityInputData data)
    {
        if (!IsPaused)
        {
            Pause();
        }
    }

    [EntityInput("UnpauseTimer")]
    private void InputUnpauseTimer(EntityInputData data)
    {
        if (!IsPaused)
        {
            return;
        }

        ScheduleAt(EntitySystem.CurrentTime + RemainingTime);
        IsPaused = false;
    }

    [EntityInput("LowerRandomBound")]
    private void InputLowerRandomBound(EntityInputData data) => lowerBound = data.Float();

    [EntityInput("UpperRandomBound")]
    private void InputUpperRandomBound(EntityInputData data) => upperBound = data.Float();

    [EntityInput("UseRandomTime")]
    private void InputUseRandomTime(EntityInputData data) => useRandomTime = data.Bool();

    [EntityInput("PauseAfterFiring")]
    private void InputPauseAfterFiring(EntityInputData data) => pauseAfterFiring = data.Bool();

    private void Enable()
    {
        IsEnabled = true;
        RestartTimer();
    }

    private void Disable()
    {
        IsEnabled = false;
        RemainingTime = 0f;
        SetNextThink(-1f);
    }

    /// <summary>Starts a full interval from now, drawing a fresh one for a randomised timer.</summary>
    private void RestartTimer()
    {
        if (!IsEnabled)
        {
            return;
        }

        if (useRandomTime)
        {
            RefireTime = lowerBound + (Random.Shared.NextSingle() * (upperBound - lowerBound));
        }

        ScheduleAt(EntitySystem.CurrentTime + RefireTime + initialDelay);
        IsPaused = false;
    }

    /// <summary>Stops the countdown, keeping what was left of it for <c>UnpauseTimer</c>.</summary>
    private void Pause()
    {
        RemainingTime = NextThink < 0f ? 0f : MathF.Max(NextThink - EntitySystem.CurrentTime, 0f);
        SetNextThink(-1f);
        IsPaused = true;
    }

    /// <summary>Moves the next firing later, or earlier for a negative amount, but no earlier than now.</summary>
    private void ShiftTimer(float seconds)
    {
        if (!IsEnabled)
        {
            return;
        }

        // A paused timer has no firing scheduled, so the change goes to the time it will resume with
        if (IsPaused)
        {
            RemainingTime = MathF.Max(RemainingTime + seconds, 0f);
            return;
        }

        ScheduleAt((NextThink < 0f ? EntitySystem.CurrentTime : NextThink) + seconds);
    }

    private void ScheduleAt(float time)
    {
        // A think due now runs on the next tick, which is also where one scheduled in the past belongs
        time = MathF.Max(time, EntitySystem.CurrentTime + EntitySystem.TickInterval);

        SetNextThink(time);
        RemainingTime = time - EntitySystem.CurrentTime;
    }
}

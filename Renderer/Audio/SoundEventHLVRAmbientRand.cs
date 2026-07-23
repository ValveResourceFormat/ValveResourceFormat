using System.Diagnostics;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_ambient_rand" sound event type: not a track picker itself, but a spawner that
/// periodically (re)starts another named sound event ("random_soundevent_01_name") at a random point
/// within "rand_radius_min".."rand_radius_max" of this event's position, or of the listener when this
/// event has none (as when played unspatialized for a scripted soundscape's ambient bed).
/// </summary>
/// <remarks>
/// Only the "01" slot has been observed in practice; if a real event ever authors "_02"/"_03" siblings
/// (matching the numbered-slot convention <see cref="SoundEventHLVRMulti"/> uses for its children), this
/// only needs a loop over these fields added, not a redesign.
/// </remarks>
internal sealed class SoundEventHLVRAmbientRand : SoundEvent
{
    private readonly string childEventName;
    private readonly float timerMin;
    private readonly float timerMax;
    private readonly float radiusMin;
    private readonly float radiusMax;
    private readonly bool positionRandom;

    private bool wasInitialized;
    private bool waitingForRetrigger;
    private long retriggerTimestamp;
    private SoundEvent? child;

    private protected override bool WaitingToStart => waitingForRetrigger;

    public SoundEventHLVRAmbientRand(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        childEventName = data.GetStringProperty("random_soundevent_01_name", string.Empty);
        timerMin = data.GetFloatProperty("random_soundevent_01_timer_min", 6f);
        timerMax = data.GetFloatProperty("random_soundevent_01_timer_max", 12f);
        radiusMin = data.GetFloatProperty("rand_radius_min");
        radiusMax = data.GetFloatProperty("rand_radius_max");
        positionRandom = data.GetFloatProperty("position_random") != 0f;
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        if (!wasInitialized && CheckRetrigger())
        {
            // Waits out its first interval before spawning, same as every other retriggered event here
            wasInitialized = true;
            return;
        }

        wasInitialized = true;

        if (childEventName.Length == 0)
        {
            return;
        }

        // Child definition is resolved through the bank once and kept on the parent definition
        var childDefinitions = Definition.ChildDefinitions ??= [Mixer.Player.Bank.GetSoundEvent(childEventName)];
        var childDefinition = childDefinitions[0];

        if (childDefinition == null)
        {
            return;
        }

        // Same instance is restarted on every retrigger rather than rebuilt from scratch
        child ??= Build(childDefinition);

        if (child == null)
        {
            return;
        }

        var anchor = Position ?? Mixer.ListenerPosition;
        child.Position = positionRandom ? PickRandomPosition(anchor) : anchor;

        StartAsChild(child);
    }

    protected override void OnFinished()
    {
        base.OnFinished();

        if (!FadingOut)
        {
            CheckRetrigger();
        }
    }

    private bool CheckRetrigger()
    {
        var retriggerAt = float.Lerp(timerMin, timerMax, Random.NextSingle());
        retriggerTimestamp = Stopwatch.GetTimestamp() + (long)(retriggerAt * Stopwatch.Frequency);
        waitingForRetrigger = true;
        return true;
    }

    /// <inheritdoc/>
    public override bool Update(Vector3 listenerPosition, Vector3 rightEarDirection)
    {
        if (Started && !FadingOut && waitingForRetrigger && Stopwatch.GetTimestamp() >= retriggerTimestamp)
        {
            waitingForRetrigger = false;
            Start();
        }

        return base.Update(listenerPosition, rightEarDirection);
    }

    private Vector3 PickRandomPosition(Vector3 anchor)
    {
        var angle = Random.NextSingle() * MathF.Tau;
        var distance = float.Lerp(radiusMin, radiusMax, Random.NextSingle());

        return anchor + new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);
    }
}

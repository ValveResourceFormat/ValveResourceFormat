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

    private SoundEvent? child;

    private protected override (float Min, float Max)? RetriggerInterval => (timerMin, timerMax);

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
        if (WaitOutFirstInterval())
        {
            return;
        }

        if (childEventName.Length == 0)
        {
            return;
        }

        var childDefinition = (Definition.ChildDefinitions ??= ResolveChildDefinitions([childEventName]))[0];

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

    internal override void Prewarm(int depth)
    {
        if (childEventName.Length == 0 || depth > MaxRecursionDepth)
        {
            return;
        }

        var childDefinition = (Definition.ChildDefinitions ??= ResolveChildDefinitions([childEventName]))[0];

        if (childDefinition == null)
        {
            return;
        }

        child ??= Build(childDefinition);

        if (child != null)
        {
            PrewarmChild(child, depth);
        }
    }

    internal override void ResetForReplay()
    {
        base.ResetForReplay();
        // The child lives in this class's own field, not the base's child slots, so cascade by hand
        child?.ResetForReplay();
    }

    private protected override bool StayAliveAfterFinishing() => CheckRetrigger();

    private Vector3 PickRandomPosition(Vector3 anchor)
    {
        var angle = Random.NextSingle() * MathF.Tau;
        var distance = float.Lerp(radiusMin, radiusMax, Random.NextSingle());

        return anchor + new Vector3(MathF.Cos(angle) * distance, MathF.Sin(angle) * distance, 0f);
    }
}

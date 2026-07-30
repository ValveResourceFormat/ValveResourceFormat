using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_start_multi_switch" sound event type: a weighted coin flip between exactly two
/// children ("soundevent_01"/"soundevent_02"), picked fresh on every play. "soundevent_split" is the
/// probability of picking "soundevent_01" (defaults to an even 50/50 split when absent).
/// </summary>
internal sealed class SoundEventHLVRSwitch : SoundEvent
{
    private readonly string?[] childEventNames = new string?[2];
    private readonly float split;
    private readonly SoundEventDefinition?[] toStart = new SoundEventDefinition?[2];

    public SoundEventHLVRSwitch(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        childEventNames[0] = data.GetStringProperty("soundevent_01");
        childEventNames[1] = data.GetStringProperty("soundevent_02");
        split = data.GetFloatProperty("soundevent_split", 0.5f);
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildren();
        var picked = Random.NextSingle() < split ? 0 : 1;

        // Only the picked slot is non-null this call, so StartChildren plays just that one; an already
        // built child instance for the other slot (from an earlier pick) is simply left untouched.
        // The scratch array is reused across starts to keep the per-play path allocation-free.
        toStart[0] = null;
        toStart[1] = null;
        toStart[picked] = childDefinitions[picked];

        StartChildren(toStart);
    }

    internal override void Prewarm(int depth)
    {
        // Both slots, since any play can pick either
        PrewarmChildren(Definition.ChildDefinitions ??= ResolveChildren(), depth);
    }

    protected override void OnFinished()
    {
        base.OnFinished();

        if (FadingOut)
        {
            // The base already completed the stop
            return;
        }

        // One-shot: fully stop once nothing in the tree can produce samples anymore, so the event
        // leaves the mixer's active set instead of staying registered (and updated) forever.
        if (!AnyChildStarted())
        {
            Stop();
        }
    }

    private SoundEventDefinition?[] ResolveChildren()
    {
        var definitions = new SoundEventDefinition?[2];

        for (var i = 0; i < 2; i++)
        {
            var name = childEventNames[i];

            if (name != null)
            {
                definitions[i] = Mixer.Player.Bank.GetSoundEvent(name);
            }
        }

        return definitions;
    }
}

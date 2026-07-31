using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_start_multi_switch" sound event type: a weighted coin flip between exactly two
/// children ("soundevent_01"/"soundevent_02"), picked fresh on every play. "soundevent_split" is the
/// probability of picking "soundevent_01" (defaults to an even 50/50 split when absent).
/// </summary>
internal sealed class SoundEventHLVRSwitch : SoundEvent
{
    private readonly string[] childEventNames = new string[2];
    private readonly float split;
    private readonly SoundEventDefinition?[] toStart = new SoundEventDefinition?[2];

    public SoundEventHLVRSwitch(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        // Empty resolves to no definition, same as a name the bank does not know
        childEventNames[0] = data.GetStringProperty("soundevent_01", string.Empty);
        childEventNames[1] = data.GetStringProperty("soundevent_02", string.Empty);
        split = data.GetFloatProperty("soundevent_split", 0.5f);
    }

    protected override void DoStart()
    {
        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames);
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
        PrewarmChildren(Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames), depth);
    }
}

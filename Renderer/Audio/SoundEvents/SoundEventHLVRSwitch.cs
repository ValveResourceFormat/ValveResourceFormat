using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Sound event type "hlvr_start_multi_switch": a weighted coin flip between "soundevent_01" and
/// "soundevent_02" per play. "soundevent_split" is the chance of "soundevent_01" (default 0.5).
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
        split = data.GetSoundFloat("soundevent_split", 0.5f);
    }

    protected override void DoStart()
    {
        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames);
        var picked = Random.NextSingle() < split ? 0 : 1;

        // Only the picked slot is non-null, so StartChildren plays just that one. A child built for
        // the other slot by an earlier pick is left alone. The scratch array is reused to keep plays
        // allocation-free.
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

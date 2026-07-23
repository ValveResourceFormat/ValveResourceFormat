using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_start_multi" sound event type: starts every child listed in
/// "soundevent_01".."soundevent_24" whose matching "use_NN" flag (only authored on the first few slots)
/// isn't explicitly disabled. Also implements "hlvr_start_multi_quad", which uses the same child-list
/// shape (its 4 slots are the L/R/LS/RS channels of a quad-panned ambient bed) plus an optional
/// "rand_delay_min"/"rand_delay_max" that staggers each child's <see cref="SoundEvent.DelayOverride"/> so
/// near-identical channels don't loop in phase lock; absent on plain "hlvr_start_multi" events, where it
/// has no effect. A simplified stand-in for the other "hlvr_start_multi_*" variants (switch, sequence,
/// distance delay, ...), which pick or sequence among their children instead of starting all of them.
/// </summary>
internal sealed class SoundEventHLVRMulti : SoundEvent
{
    private const int MaxSlots = 24;

    private readonly bool hasRandomDelay;
    private readonly float randDelayMin;
    private readonly float randDelayMax;

    public SoundEventHLVRMulti(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        hasRandomDelay = data.ContainsKey("rand_delay_min") || data.ContainsKey("rand_delay_max");
        randDelayMin = data.GetFloatProperty("rand_delay_min");
        randDelayMax = data.GetFloatProperty("rand_delay_max");
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildren();
        StartChildren(childDefinitions, hasRandomDelay ? StaggerChild : null);
    }

    private void StaggerChild(SoundEvent child, int index)
    {
        child.DelayOverride = float.Lerp(randDelayMin, randDelayMax, Random.NextSingle());
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
        var data = Definition.Data;
        var names = new List<string>();

        for (var i = 1; i <= MaxSlots; i++)
        {
            var suffix = i.ToString("D2", CultureInfo.InvariantCulture);
            var name = data.GetStringProperty("soundevent_" + suffix);

            if (name == null)
            {
                continue;
            }

            // Only the first few slots carry an optional "use_NN" toggle; a missing flag means enabled.
            if (data.ContainsKey("use_" + suffix) && data.GetFloatProperty("use_" + suffix) == 0f)
            {
                continue;
            }

            names.Add(name);
        }

        var definitions = new SoundEventDefinition?[names.Count];

        for (var i = 0; i < names.Count; i++)
        {
            definitions[i] = Mixer.Player.Bank.GetSoundEvent(names[i]);
        }

        return definitions;
    }
}

using System.Globalization;
using ValveKeyValue;
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

    private readonly float randDelayMin;
    private readonly float randDelayMax;
    private readonly bool hasRandomDelay;
    private readonly string[] childEventNames;
    // Per-slot "volume_soundevent_NN", parallel to childEventNames, negative where unauthored
    private readonly float[] childVolumes;
    // Cached once: a method group passed straight to StartChildren would allocate a delegate per start
    private readonly Action<SoundEvent, int>? applyChild;

    public SoundEventHLVRMulti(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        hasRandomDelay = data.ContainsKey("rand_delay_min") || data.ContainsKey("rand_delay_max");
        randDelayMin = data.GetFloatProperty("rand_delay_min");
        randDelayMax = data.GetFloatProperty("rand_delay_max");
        applyChild = ApplyChild;

        // Collected here rather than alongside the bank lookup in ResolveChildren: that one only runs for
        // whichever instance resolves the shared Definition.ChildDefinitions first, so a second concurrent
        // instance of the same definition would be left without any per-slot volumes.
        (childEventNames, childVolumes) = CollectChildren(data);
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildren();
        StartChildren(childDefinitions, applyChild);
    }

    internal override void Prewarm(int depth)
    {
        PrewarmChildren(Definition.ChildDefinitions ??= ResolveChildren(), depth);
    }

    private void ApplyChild(SoundEvent child, int index)
    {
        if (hasRandomDelay)
        {
            child.DelayOverride = float.Lerp(randDelayMin, randDelayMax, Random.NextSingle());
        }

        if (index < childVolumes.Length && childVolumes[index] >= 0f)
        {
            child.VolumeOverride = childVolumes[index];
        }
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

    /// <summary>Collects the child event names this definition starts, with their per-slot volumes.</summary>
    private static (string[] Names, float[] Volumes) CollectChildren(KVObject data)
    {
        var names = new List<string>();
        var volumes = new List<float>();

        // "hlvr_animate_soundevent" lists its children in an array instead of numbered slots
        foreach (var name in GetStringOrArrayProperty(data, "soundevents"))
        {
            names.Add(name);
            volumes.Add(-1f);
        }

        // "hlvr_start_multi_bullet" names its two children instead of numbering them
        var core = data.GetStringProperty("soundevent_core");

        if (!string.IsNullOrEmpty(core))
        {
            names.Add(core);
            volumes.Add(-1f);

            var damage = data.GetStringProperty("soundevent_damage");

            if (!string.IsNullOrEmpty(damage) && data.GetFloatProperty("use_soundevent_damage") != 0f)
            {
                names.Add(damage);
                volumes.Add(-1f);
            }
        }

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

            // Negative means unauthored, leaving the child's own volume alone
            volumes.Add(data.ContainsKey("volume_soundevent_" + suffix)
                ? data.GetFloatProperty("volume_soundevent_" + suffix)
                : -1f);
        }

        return ([.. names], [.. volumes]);
    }

    private SoundEventDefinition?[] ResolveChildren()
    {
        var definitions = new SoundEventDefinition?[childEventNames.Length];

        for (var i = 0; i < childEventNames.Length; i++)
        {
            definitions[i] = Mixer.Player.Bank.GetSoundEvent(childEventNames[i]);
        }

        return definitions;
    }
}

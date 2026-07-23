using System.Globalization;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_start_multi" sound event type: starts every child listed in
/// "soundevent_01".."soundevent_24" whose matching "use_NN" flag (only authored on the first few slots)
/// isn't explicitly disabled. A simplified stand-in for the many "hlvr_start_multi_*" variants (switch,
/// sequence, quad, distance delay, ...), which pick or sequence among their children instead of
/// starting all of them together.
/// </summary>
internal sealed class SoundEventHLVRMulti : SoundEvent
{
    private const int MaxSlots = 24;

    public SoundEventHLVRMulti(SoundEventDefinition definition) : base(definition)
    {
    }

    protected override void DoStart()
    {
        if (Position == null && Definition.Position.HasValue)
        {
            Position = Definition.Position;
        }

        PositionOffset = Definition.PositionOffset;

        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildren();
        StartChildren(childDefinitions);
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

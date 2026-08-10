using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Implements the "hlvr_start_soundevent" type: starts one event out of its "soundevents" list, picked at
/// random or taken in order per "sequence_type", and picks again when it finishes if "restart_on_finish"
/// is set (a radio station cycling through its lines).
/// </summary>
internal sealed class SoundEventHLVRStartSoundEvent : SoundEvent
{
    private readonly string[] childEventNames;
    private readonly bool sequential;
    private readonly bool restartOnFinish;
    private readonly float volumeAttenuation;

    private int nextIndex;
    private bool restartPending;

    private protected override bool WaitingToStart => restartPending;

    public SoundEventHLVRStartSoundEvent(SoundEventDefinition definition) : base(definition)
    {
        var data = definition.Data;

        childEventNames = GetStringOrArrayProperty(data, "soundevents");
        sequential = !string.Equals(data.GetStringProperty("sequence_type"), "random", StringComparison.OrdinalIgnoreCase);
        restartOnFinish = data.GetBooleanProperty("restart_on_finish");
        volumeAttenuation = data.GetFloatProperty("volume_atten", 1f);
    }

    protected override void DoStart()
    {
        if (childEventNames.Length == 0)
        {
            return;
        }

        var childDefinitions = Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames);

        var index = sequential
            ? nextIndex % childDefinitions.Length
            : Mixer.Player.PickTrack(Definition, childDefinitions.Length);

        nextIndex = index + 1;

        var child = GetOrBuildChild(childDefinitions, index);

        if (child == null)
        {
            return;
        }

        child.VolumeOverride = Math.Clamp((VolumeOverride ?? Definition.Volume) * volumeAttenuation, 0f, 1f);
        StartAsChild(child);
    }

    internal override void Prewarm(int depth)
    {
        if (childEventNames.Length > 0)
        {
            PrewarmChildren(Definition.ChildDefinitions ??= ResolveChildDefinitions(childEventNames), depth);
        }
    }

    private protected override bool StayAliveAfterFinishing()
    {
        if (restartOnFinish && childEventNames.Length > 0)
        {
            // Deferred to Update rather than restarted here: this also runs on the mixing thread (a sound
            // running dry mid-read), and Start() clears the provider/child lists that Update iterates on
            // the game thread. Every other rescheduling type defers for the same reason.
            restartPending = true;
            return true;
        }

        return AnyChildStarted();
    }

    /// <inheritdoc/>
    /// <remarks>The restart deferred by <see cref="StayAliveAfterFinishing"/> happens here, on the game thread.</remarks>
    public override bool Update(in ListenerState listener)
    {
        if (Started && !FadingOut && restartPending)
        {
            restartPending = false;
            Start();
        }

        return base.Update(listener);
    }

    internal override void ResetForReplay()
    {
        base.ResetForReplay();
        restartPending = false;
    }
}

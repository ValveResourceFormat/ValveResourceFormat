namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A reference to one play of a sound event. <see cref="SoundEvent"/> instances are pooled across plays,
/// so the handle checks <see cref="SoundEvent.PlaybackId"/> and goes inert once the instance is reused.
/// </summary>
public readonly struct SoundHandle
{
    private readonly SoundEvent? soundEvent;
    private readonly long playbackId;

    internal SoundHandle(SoundEvent soundEvent)
    {
        this.soundEvent = soundEvent;
        playbackId = soundEvent.PlaybackId;
    }

    private SoundEvent? Live => soundEvent != null && soundEvent.PlaybackId == playbackId ? soundEvent : null;

    /// <summary>Gets whether this handle refers to a play that actually started; it may have finished since.</summary>
    public bool IsValid => Live != null;

    /// <summary>Gets whether the play is active in the mixer.</summary>
    public bool Started => Live is { Started: true };

    /// <summary>Gets whether the play is currently producing audible samples.</summary>
    public bool Playing => Live is { Playing: true };

    /// <summary>Gets or sets the world position of the sound; null plays it unspatialized.</summary>
    public Vector3? Position
    {
        get => Live?.Position;
        set
        {
            if (Live is { } live)
            {
                live.Position = value;
            }
        }
    }

    /// <summary>Gets or sets a live 0..1 gain on top of the volume the play started with.</summary>
    public float Volume
    {
        get => Live?.LiveVolume ?? 0f;
        set => Live?.LiveVolume = value;
    }

    /// <summary>Fades the play in from silence over the given seconds.</summary>
    public void FadeIn(float seconds) => Live?.FadeIn(seconds);

    /// <summary>Stops the play, if it is still this handle's own.</summary>
    public void Stop() => Live?.Stop();

    /// <summary>Fades the play out and stops it.</summary>
    public void FadeOutAndStop(float fallbackSeconds = 1f) => Live?.FadeOutAndStop(fallbackSeconds);
}

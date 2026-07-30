using ValveResourceFormat.Renderer.Utils;

namespace ValveResourceFormat.Renderer.Audio.SampleProviders;

/// <summary>
/// A source of interleaved 32-bit float audio samples in the mixer's output format.
/// </summary>
public interface IAudioSampleProvider
{
    /// <summary>
    /// Fills <paramref name="buffer"/> with up to <paramref name="count"/> samples starting at <paramref name="offset"/>.
    /// Returns the number of samples written. A return value smaller than <paramref name="count"/> signals the end of the sound.
    /// </summary>
    int Read(float[] buffer, int offset, int count);
}

/// <summary>
/// Base sample provider with a perceptual volume control and an end-of-sound notification.
/// </summary>
public abstract class AudioSampleProvider : IAudioSampleProvider
{
    /// <summary>
    /// Raised when this provider has run out of samples.
    /// </summary>
    public event Action? OnOver;

    /// <summary>
    /// The provider's reusable node for the <see cref="SampleProviderMulti"/> it plays under. A provider
    /// only ever sits in one mix at a time (the provider tree is fixed: a track provider under its event,
    /// an event's mix under its parent or the root), so one lazily created node per provider lets
    /// add/remove cycles - which happen constantly, on every play, retrigger and run-dry - reuse it
    /// instead of allocating a fresh <see cref="LinkedListNode{T}"/> on every add.
    /// </summary>
    internal LinkedListNode<IAudioSampleProvider>? MixNode;

    /// <summary>Creates <see cref="MixNode"/> ahead of the first add, so prewarmed events' first play allocates nothing.</summary>
    internal void PrewarmMixNode() => MixNode ??= new LinkedListNode<IAudioSampleProvider>(this);

    private float volume = 1f;

    /// <summary>
    /// Gets or sets the volume (0..1), mapped onto an exponential curve to approximate perceptual loudness.
    /// </summary>
    public float Volume
    {
        get => volume;
        set
        {
            volume = value;
            VolumeMultiplier = MathUtils.ToPerceptualVolume(value);
        }
    }

    /// <summary>
    /// Gets the linear gain computed from <see cref="Volume"/>.
    /// </summary>
    protected float VolumeMultiplier { get; private set; } = 1f;

    /// <inheritdoc/>
    public abstract int Read(float[] buffer, int offset, int count);

    /// <summary>
    /// Raises <see cref="OnOver"/>.
    /// </summary>
    protected void Over()
    {
        OnOver?.Invoke();
    }
}

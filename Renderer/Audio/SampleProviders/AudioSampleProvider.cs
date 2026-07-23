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

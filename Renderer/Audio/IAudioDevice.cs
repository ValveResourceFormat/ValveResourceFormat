namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// An audio output device that accepts interleaved 32-bit float samples.
/// Implementations live outside of the renderer (e.g. NAudio, SDL, OpenAL) and are injected into <see cref="SoundEventPlayer"/>,
/// keeping the renderer free of audio library dependencies.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>
    /// Gets the output sample rate in Hz. The mixer produces samples at this rate.
    /// </summary>
    int SampleRate { get; }

    /// <summary>
    /// Gets the number of output channels. The mixer currently only produces stereo (2 channels).
    /// </summary>
    int Channels { get; }

    /// <summary>
    /// Submits interleaved float samples for playback. Called continuously from the mixing thread.
    /// Implementations should block until the device can accept the samples — this is what paces the mixing thread.
    /// </summary>
    void SubmitSamples(ReadOnlySpan<float> samples);
}

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// An audio output device that accepts interleaved 32-bit float samples.
/// </summary>
public interface IAudioDevice : IDisposable
{
    /// <summary>Gets the output sample rate in Hz.</summary>
    int SampleRate { get; }

    /// <summary>Gets the number of output channels (currently always 2).</summary>
    int Channels { get; }

    /// <summary>Submits interleaved float samples for playback. Implementations should block until accepted.</summary>
    void SubmitSamples(ReadOnlySpan<float> samples);
}

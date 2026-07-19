namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Raw decoded audio in its source format.
/// </summary>
public sealed class DecodedAudio
{
    /// <summary>Gets the interleaved samples.</summary>
    public required float[] Samples { get; init; }
    /// <summary>Gets the channel count.</summary>
    public required int Channels { get; init; }
    /// <summary>Gets the sample rate in Hz.</summary>
    public required int SampleRate { get; init; }
}

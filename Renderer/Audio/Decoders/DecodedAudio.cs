namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Raw decoded audio in its source format.
/// </summary>
public sealed class DecodedAudio
{
    public required float[] Samples { get; init; }
    public required int Channels { get; init; }
    public required int SampleRate { get; init; }
}

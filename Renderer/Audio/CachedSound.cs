namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A fully decoded sound, stored as interleaved float samples in the mixer's output format.
/// </summary>
public sealed class CachedSound
{
    /// <summary>
    /// Gets the interleaved float samples in the mixer's output format (sample rate and channel count).
    /// </summary>
    public required float[] Samples { get; init; }

    /// <summary>
    /// Gets the loop start position as an index into <see cref="Samples"/>, or -1 when the sound does not loop.
    /// </summary>
    public int LoopStart { get; init; } = -1;

    /// <summary>
    /// Gets the loop end position as an exclusive index into <see cref="Samples"/>.
    /// </summary>
    public int LoopEnd { get; init; }
}

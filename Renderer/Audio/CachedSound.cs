namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A decoded sound, stored as interleaved 16-bit PCM in the mixer's sample rate and channel count.
/// PCM16 halves the memory of the float mixer format at inaudible cost; the provider converts to float on read.
/// Returned by the cache immediately as a placeholder while a background thread decodes; providers play
/// silence until <see cref="Ready"/>, so audio decoding never blocks the game or mixing threads.
/// </summary>
public sealed class CachedSound
{
    /// <summary>
    /// Gets the interleaved 16-bit PCM samples in the mixer's output format (sample rate and channel count).
    /// Empty until <see cref="Ready"/>, and stays empty when decoding failed.
    /// </summary>
    public short[] Samples { get; internal set; } = [];

    /// <summary>
    /// Gets the loop start position as an index into <see cref="Samples"/>, or -1 when the sound does not loop.
    /// </summary>
    public int LoopStart { get; internal set; } = -1;

    /// <summary>
    /// Gets the loop end position as an exclusive index into <see cref="Samples"/>.
    /// </summary>
    public int LoopEnd { get; internal set; }

    private volatile bool ready;

    /// <summary>
    /// Gets whether decoding has finished. The volatile write happens after the samples are assigned,
    /// so a reader that observes true also observes the samples.
    /// </summary>
    public bool Ready { get => ready; internal set => ready = value; }

    /// <summary>
    /// Stopwatch timestamp of the last time this sound was requested or read by the mixer. The cache uses it to
    /// avoid evicting sounds that are currently playing. Written from the mixing thread on every read and read by
    /// the cache; a plain long, which is atomic on 64-bit and only feeds an eviction heuristic.
    /// </summary>
    internal long LastUsed;
}

namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A decoded sound: interleaved 16-bit PCM at the mixer's sample rate and channel count.
/// Returned as a placeholder while a background thread decodes it.
/// </summary>
public sealed class CachedSound
{
    /// <summary>Gets the interleaved 16-bit PCM samples. Empty until <see cref="Ready"/>, or when decoding failed.</summary>
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

    /// <summary>Gets whether decoding has finished. Volatile: true implies the samples are already visible.</summary>
    public bool Ready { get => ready; internal set => ready = value; }

    private long lastUsed;

    /// <summary>
    /// Stopwatch timestamp of the last read by the mixer. Volatile so the 64-bit value cannot tear on 32-bit runtimes.
    /// </summary>
    internal long LastUsed
    {
        get => System.Threading.Volatile.Read(ref lastUsed);
        set => System.Threading.Volatile.Write(ref lastUsed, value);
    }
}

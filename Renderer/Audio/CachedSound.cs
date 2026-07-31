namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// A decoded sound: interleaved 16-bit PCM at the mixer's sample rate and channel count, stored as a
/// region of a shared <see cref="SampleArena"/> slab rather than its own array.
/// Returned as a placeholder while a background thread decodes it.
/// </summary>
public sealed class CachedSound
{
    /// <summary>
    /// Gets the slab holding the samples. Shared with other sounds: only
    /// [<see cref="SampleOffset"/>, <see cref="SampleOffset"/> + <see cref="SampleLength"/>) belongs to
    /// this sound. Empty until <see cref="Ready"/>, or when decoding failed.
    /// </summary>
    public short[] Samples { get; internal set; } = [];

    /// <summary>Gets this sound's first sample index within <see cref="Samples"/>.</summary>
    public int SampleOffset { get; internal set; }

    /// <summary>Gets this sound's sample count.</summary>
    public int SampleLength { get; internal set; }

    /// <summary>The arena slab this sound's region belongs to, for returning it on eviction.</summary>
    internal int SlabIndex = -1;

    /// <summary>
    /// Gets the loop start position relative to <see cref="SampleOffset"/>, or -1 when the sound does not loop.
    /// </summary>
    public int LoopStart { get; internal set; } = -1;

    /// <summary>
    /// Gets the loop end position (exclusive) relative to <see cref="SampleOffset"/>.
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

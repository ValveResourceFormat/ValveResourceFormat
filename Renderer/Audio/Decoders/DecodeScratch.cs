namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Reusable per-thread scratch buffers for the decode pipeline. Decoding is a byte/float relay race
/// whose buffers all die as soon as the <see cref="Pcm16ArenaSink"/> has produced the final
/// cached PCM16 - without reuse, every decode churns multiples of the sound's size in garbage
/// (dominating profiler captures whenever new sounds stream in). The decode threads are two long-lived
/// dedicated threads, so a grow-only thread-local buffer serves every decode after its high-water mark
/// with zero allocation. Oversized requests (rare multi-minute music tracks) are served with one-off
/// arrays instead, so no thread permanently pins tens of megabytes.
/// </summary>
internal static class DecodeScratch
{
    private const int MaxRetainedBytes = 16 * 1024 * 1024;

    private const int PrewarmBytes = 1024 * 1024;

    [ThreadStatic]
    private static float[]? floats;

    [ThreadStatic]
    private static byte[]? bytes;

    public static void Prewarm()
    {
        RentFloats(PcmDecoder.ChunkSamples);
        RentBytes(PrewarmBytes);
    }

    /// <summary>
    /// Gets a float buffer of at least <paramref name="minimumLength"/> elements. Contents are undefined.
    /// Valid until the current thread's next call to any method here.
    /// </summary>
    public static float[] RentFloats(int minimumLength)
    {
        var current = floats;

        if (current != null && current.Length >= minimumLength)
        {
            return current;
        }

        if ((long)minimumLength * sizeof(float) > MaxRetainedBytes)
        {
            return new float[minimumLength];
        }

        return floats = new float[RoundUpPow2(minimumLength)];
    }

    /// <summary>
    /// Gets a byte buffer of at least <paramref name="minimumLength"/> elements. Contents are undefined.
    /// Valid until the current thread's next call to any method here.
    /// </summary>
    public static byte[] RentBytes(int minimumLength)
    {
        var current = bytes;

        if (current != null && current.Length >= minimumLength)
        {
            return current;
        }

        if (minimumLength > MaxRetainedBytes)
        {
            return new byte[minimumLength];
        }

        return bytes = new byte[RoundUpPow2(minimumLength)];
    }

    private static int RoundUpPow2(int value) => (int)System.Numerics.BitOperations.RoundUpToPowerOf2((uint)Math.Max(value, 4096));
}

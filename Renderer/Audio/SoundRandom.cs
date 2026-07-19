namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Small allocation-free random source for sound event randomization (xorshift64).
/// A single instance lives on the <see cref="SoundEventPlayer"/> and is reseeded in place on every play.
/// </summary>
internal sealed class SoundRandom
{
    private ulong state = 0x9E3779B97F4A7C15UL;

    /// <summary>
    /// Reseeds the sequence in place, e.g. with the play timestamp.
    /// </summary>
    public void Reseed(long seed)
    {
        // splitmix64 scramble so consecutive timestamps produce unrelated sequences
        var z = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;
        state = z ^ (z >> 31) | 1;
    }

    private ulong NextUInt64()
    {
        var x = state;
        x ^= x << 13;
        x ^= x >> 7;
        x ^= x << 17;
        state = x;
        return x;
    }

    /// <summary>
    /// Returns a random integer in [0, <paramref name="maxExclusive"/>).
    /// </summary>
    public int Next(int maxExclusive) => (int)((NextUInt64() >> 33) % (uint)maxExclusive);

    /// <summary>
    /// Returns a random float in [0, 1).
    /// </summary>
    public float NextSingle() => (NextUInt64() >> 40) * (1f / (1 << 24));
}

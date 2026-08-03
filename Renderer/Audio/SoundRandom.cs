namespace ValveResourceFormat.Renderer.Audio;

/// <summary>
/// Small allocation-free random source (xorshift64), reseeded in place on every play.
/// </summary>
/// <remarks>
/// One instance is shared by every event playing through a <see cref="SoundEventPlayer"/>, and it is
/// deliberately unsynchronized: the game thread reseeds it and draws from it (play, retrigger timers,
/// occlusion jitter), and the mixing thread draws from it too when a sound running dry mid-read arms its
/// next replay. A torn read or a lost update there costs sequence quality, not correctness - every
/// consumer only wants "some value in this range" - and the alternative, a lock or an interlocked state
/// update on the mixing thread, would buy nothing audible.
/// </remarks>
internal sealed class SoundRandom
{
    private ulong state = 0x9E3779B97F4A7C15UL;

    /// <summary>Reseeds the sequence in place.</summary>
    public void Reseed(long seed)
    {
        // splitmix64 scramble so consecutive timestamps produce unrelated sequences
        var z = unchecked((ulong)seed + 0x9E3779B97F4A7C15UL);
        z = (z ^ (z >> 30)) * 0xBF58476D1CE4E5B9UL;
        z = (z ^ (z >> 27)) * 0x94D049BB133111EBUL;

        // The trailing | 1 keeps the state off zero, which xorshift can never leave
        state = (z ^ (z >> 31)) | 1;
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

    /// <summary>Returns a random integer in [0, <paramref name="maxExclusive"/>).</summary>
    public int Next(int maxExclusive) => (int)((NextUInt64() >> 33) % (uint)maxExclusive);

    /// <summary>Returns a random float in [0, 1).</summary>
    public float NextSingle() => (NextUInt64() >> 40) * (1f / (1 << 24));
}

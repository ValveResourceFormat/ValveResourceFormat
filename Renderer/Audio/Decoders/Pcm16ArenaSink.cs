namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Receives decoded audio as a stream of interleaved float chunks.
/// Decoders push through this instead of materializing whole files, so decode memory stays flat
/// (one scratch chunk) no matter how long the sound is.
/// </summary>
internal interface IPcm16Sink
{
    /// <summary>
    /// Reports the source format before the first <see cref="Write"/>.
    /// <paramref name="estimatedSourceSamples"/> sizes the output up front (0 or negative when unknown).
    /// </summary>
    void SetFormat(int channels, int sampleRate, long estimatedSourceSamples);

    /// <summary>Consumes the next run of interleaved source samples. Must contain whole frames.</summary>
    void Write(ReadOnlySpan<float> samples);
}

/// <summary>
/// Converts pushed float chunks to the mixer's channel count and sample rate (linear interpolation
/// with cross-chunk carry), quantizes to 16-bit PCM with TPDF dither, and writes the result straight
/// into a <see cref="SampleArena"/> region - the final resting place of the cached sound - growing or
/// trimming the region as the real decoded length reveals itself.
/// </summary>
internal sealed class Pcm16ArenaSink : IPcm16Sink
{
    private readonly SampleArena arena;
    private readonly int targetChannels;
    private readonly int targetRate;
    private readonly long sourceFrameHint;

    private short[] slab = [];
    private int slabIndex = -1;
    private int offset;
    private int capacity;
    private int written;

    private int sourceChannels;
    private double step;
    private double nextSourcePos;
    private long consumedFrames;
    private readonly float[] carryFrame;
    private bool hasCarry;

    /// <summary>Gets the source sample rate reported by the decoder.</summary>
    public int SourceRate { get; private set; }

    /// <summary>Gets how many source frames have been consumed, for duration validation.</summary>
    public long SourceFramesConsumed => consumedFrames;

    /// <summary>Gets the number of samples written to the arena so far.</summary>
    public int WrittenSamples => written;

    /// <param name="arena">The arena the final PCM lives in.</param>
    /// <param name="targetChannels">Mixer channel count.</param>
    /// <param name="targetRate">Mixer sample rate.</param>
    /// <param name="sourceFrameHint">
    /// Expected source frame count from the sound resource, taking precedence over the decoder's own
    /// estimate for sizing the region (0 or negative when unknown).
    /// </param>
    public Pcm16ArenaSink(SampleArena arena, int targetChannels, int targetRate, long sourceFrameHint)
    {
        this.arena = arena;
        this.targetChannels = targetChannels;
        this.targetRate = targetRate;
        this.sourceFrameHint = sourceFrameHint;
        carryFrame = new float[targetChannels];
    }

    /// <inheritdoc/>
    public void SetFormat(int channels, int sampleRate, long estimatedSourceSamples)
    {
        sourceChannels = channels;
        SourceRate = sampleRate;
        step = (double)sampleRate / targetRate;

        var estimatedFrames = sourceFrameHint > 0
            ? sourceFrameHint
            : estimatedSourceSamples > 0 ? estimatedSourceSamples / channels : 65536;

        var targetSamples = ((long)(estimatedFrames * (double)targetRate / sampleRate) + 16) * targetChannels;
        var allocation = arena.Allocate((int)Math.Clamp(targetSamples, 4096, int.MaxValue / 2));

        (slab, slabIndex, offset, capacity) = (allocation.Slab, allocation.SlabIndex, allocation.Offset, allocation.Length);
    }

    /// <inheritdoc/>
    public void Write(ReadOnlySpan<float> samples)
    {
        var chunkFrames = samples.Length / sourceChannels;
        if (chunkFrames == 0)
        {
            return;
        }

        var random = Random.Shared;

        if (SourceRate == targetRate)
        {
            EnsureCapacity(written + chunkFrames * targetChannels);

            for (var frame = 0; frame < chunkFrames; frame++)
            {
                for (var ch = 0; ch < targetChannels; ch++)
                {
                    var value = samples[frame * sourceChannels + Math.Min(ch, sourceChannels - 1)];
                    slab[offset + written++] = Quantize(value, random);
                }
            }

            consumedFrames += chunkFrames;
            return;
        }

        // Resample: frames [consumedFrames - (hasCarry ? 1 : 0), consumedFrames + chunkFrames) are on hand
        var chunkEnd = consumedFrames + chunkFrames;

        while (true)
        {
            var frame0 = (long)nextSourcePos;

            // frame1 = frame0 + 1 must be inside this chunk; the final source frame is interpolated
            // against itself once the stream ends via the carry in the next (absent) chunk - matching
            // the old whole-file converter's clamp - so a tiny tail may go unemitted. Inaudible.
            if (frame0 + 1 >= chunkEnd || frame0 < consumedFrames - (hasCarry ? 1 : 0))
            {
                break;
            }

            var t = (float)(nextSourcePos - frame0);
            EnsureCapacity(written + targetChannels);

            for (var ch = 0; ch < targetChannels; ch++)
            {
                var sourceChannel = Math.Min(ch, sourceChannels - 1);

                var s0 = frame0 < consumedFrames
                    ? carryFrame[ch]
                    : samples[(int)(frame0 - consumedFrames) * sourceChannels + sourceChannel];
                var s1 = samples[(int)(frame0 + 1 - consumedFrames) * sourceChannels + sourceChannel];

                slab[offset + written++] = Quantize(float.Lerp(s0, s1, t), random);
            }

            nextSourcePos += step;
        }

        for (var ch = 0; ch < targetChannels; ch++)
        {
            carryFrame[ch] = samples[(chunkFrames - 1) * sourceChannels + Math.Min(ch, sourceChannels - 1)];
        }

        hasCarry = true;
        consumedFrames = chunkEnd;
    }

    /// <summary>
    /// Finalizes the region: returns any over-estimated tail to the arena and reports where the sound lives.
    /// </summary>
    public SampleArena.Allocation Finish()
    {
        arena.FreeTail(slabIndex, offset, written, capacity);
        return new SampleArena.Allocation(slab, slabIndex, offset, written);
    }

    /// <summary>Returns the whole region to the arena after a failed decode.</summary>
    public void Abandon()
    {
        if (slabIndex >= 0)
        {
            arena.Free(slabIndex, offset, capacity);
            slabIndex = -1;
            slab = [];
            capacity = 0;
            written = 0;
        }
    }

    private void EnsureCapacity(int neededSamples)
    {
        if (neededSamples <= capacity)
        {
            return;
        }

        // The estimate came in low (e.g. a header-less MP3): move to a half-again larger region
        var grown = arena.Allocate(Math.Max(neededSamples, capacity + capacity / 2));
        Array.Copy(slab, offset, grown.Slab, grown.Offset, written);
        arena.Free(slabIndex, offset, capacity);

        (slab, slabIndex, offset, capacity) = (grown.Slab, grown.SlabIndex, grown.Offset, grown.Length);
    }

    // Random.Shared rather than the player's SoundRandom: this runs on the decode threads, where the
    // shared instance's thread safety is worth more than matching the rest of the audio system, and
    // dither wants to be uncorrelated with anything else anyway.
    private static short Quantize(float value, Random random)
    {
        // Difference of two uniform [0,1) draws gives a triangular distribution over (-1, 1) LSB
        var dither = random.NextSingle() - random.NextSingle();
        return (short)Math.Clamp(MathF.Round(value * 32767f + dither), short.MinValue, short.MaxValue);
    }
}

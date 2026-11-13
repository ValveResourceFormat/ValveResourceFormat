namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Receives decoded audio as interleaved float chunks, so decode memory stays flat
/// (one scratch chunk) regardless of sound length.
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
/// into a <see cref="SampleArena"/> region, which holds the cached sound, growing or
/// trimming the region to the real decoded length.
/// </summary>
/// <param name="arena">The arena the final PCM lives in.</param>
/// <param name="targetChannels">Mixer channel count.</param>
/// <param name="targetRate">Mixer sample rate.</param>
/// <param name="sourceFrameHint">
/// Expected source frame count from the sound resource, taking precedence over the decoder's own
/// estimate for sizing the region (0 or negative when unknown).
/// </param>
internal sealed class Pcm16ArenaSink(SampleArena arena, int targetChannels, int targetRate, long sourceFrameHint) : IPcm16Sink
{
    private readonly float[] carryFrame = new float[targetChannels];
    private SampleArena.Allocation region;

    private int sourceChannels;
    private double step;
    private double nextSourcePos;
    private bool hasCarry;

    /// <summary>Gets the source sample rate reported by the decoder.</summary>
    public int SourceRate { get; private set; }

    /// <summary>Gets how many source frames have been consumed, for duration validation.</summary>
    public long SourceFramesConsumed { get; private set; }

    /// <summary>Gets the number of samples written to the arena so far.</summary>
    public int WrittenSamples { get; private set; }

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

        region = arena.Allocate((int)Math.Clamp(targetSamples, 4096, int.MaxValue / 2));
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
            EnsureCapacity(WrittenSamples + chunkFrames * targetChannels);

            for (var frame = 0; frame < chunkFrames; frame++)
            {
                for (var ch = 0; ch < targetChannels; ch++)
                {
                    Emit(Source(samples, frame, ch), random);
                }
            }

            SourceFramesConsumed += chunkFrames;
            return;
        }

        // Resample: frames [firstFrame - (hasCarry ? 1 : 0), firstFrame + chunkFrames) are on hand
        var firstFrame = SourceFramesConsumed;
        var chunkEnd = firstFrame + chunkFrames;

        while (true)
        {
            var frame0 = (long)nextSourcePos;

            // frame1 = frame0 + 1 must be inside this chunk; the final source frame is interpolated
            // against itself once the stream ends via the carry in the next (absent) chunk, so a tiny
            // tail may go unemitted. Inaudible.
            if (frame0 + 1 >= chunkEnd || frame0 < firstFrame - (hasCarry ? 1 : 0))
            {
                break;
            }

            var t = (float)(nextSourcePos - frame0);
            var frame = (int)(frame0 - firstFrame);

            EnsureCapacity(WrittenSamples + targetChannels);

            for (var ch = 0; ch < targetChannels; ch++)
            {
                var s0 = frame < 0 ? carryFrame[ch] : Source(samples, frame, ch);
                var s1 = Source(samples, frame + 1, ch);

                Emit(MathUtils.Lerp(s0, s1, t), random);
            }

            nextSourcePos += step;
        }

        for (var ch = 0; ch < targetChannels; ch++)
        {
            carryFrame[ch] = Source(samples, chunkFrames - 1, ch);
        }

        hasCarry = true;
        SourceFramesConsumed = chunkEnd;
    }

    /// <summary>
    /// Finalizes the region: returns any over-estimated tail to the arena and reports where the sound lives.
    /// </summary>
    public SampleArena.Allocation Finish()
    {
        arena.FreeTail(region.SlabIndex, region.Offset, WrittenSamples, region.Length);
        return region with { Length = WrittenSamples };
    }

    /// <summary>Returns the whole region to the arena after a failed decode.</summary>
    public void Abandon()
    {
        arena.Free(region.SlabIndex, region.Offset, region.Length);
        region = default;
        WrittenSamples = 0;
    }

    // A source with fewer channels than the mixer repeats its last one
    private float Source(ReadOnlySpan<float> samples, int frame, int channel)
        => samples[frame * sourceChannels + Math.Min(channel, sourceChannels - 1)];

    private void Emit(float value, Random random)
        => region.Slab[region.Offset + WrittenSamples++] = Quantize(value, random);

    private void EnsureCapacity(int neededSamples)
    {
        if (neededSamples <= region.Length)
        {
            return;
        }

        // The estimate came in low (e.g. a header-less MP3): move to a half-again larger region
        var grown = arena.Allocate(Math.Max(neededSamples, region.Length + region.Length / 2));
        Array.Copy(region.Slab, region.Offset, grown.Slab, grown.Offset, WrittenSamples);
        arena.Free(region.SlabIndex, region.Offset, region.Length);

        region = grown;
    }

    // Random.Shared rather than the player's SoundRandom: this runs on decode threads, where thread
    // safety outweighs matching the rest of the audio system. Dither should be uncorrelated anyway.
    private static short Quantize(float value, Random random)
    {
        // Difference of two uniform [0,1) draws gives a triangular distribution over (-1, 1) LSB
        var dither = random.NextSingle() - random.NextSingle();
        return (short)Math.Clamp(MathF.Round(value * 32767f + dither), short.MinValue, short.MaxValue);
    }
}

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Converts decoded audio into the mixer's output format (channel count and sample rate).
/// </summary>
public static class AudioConverter
{
    /// <summary>
    /// Quantizes interleaved float samples to interleaved 16-bit PCM, halving the memory of a cached sound.
    /// Applies TPDF dither (triangular, ±1 LSB) so the quantization error is decorrelated noise rather than
    /// distortion, keeping quiet fades and tails clean. Values are rounded and clamped, so inputs slightly
    /// outside [-1, 1] (e.g. from MP3 decoding) do not wrap.
    /// </summary>
    public static short[] ToPcm16(float[] samples)
    {
        var output = new short[samples.Length];
        var random = Random.Shared;

        for (var i = 0; i < samples.Length; i++)
        {
            // Difference of two uniform [0,1) draws gives a triangular distribution over (-1, 1) LSB
            var dither = random.NextSingle() - random.NextSingle();
            output[i] = (short)Math.Clamp(MathF.Round(samples[i] * 32767f + dither), short.MinValue, short.MaxValue);
        }

        return output;
    }

    /// <summary>
    /// Converts interleaved samples to the target channel count and sample rate using linear interpolation.
    /// </summary>
    public static float[] Convert(float[] samples, int sourceChannels, int sourceRate, int targetChannels, int targetRate)
    {
        if (sourceChannels != targetChannels)
        {
            samples = ConvertChannels(samples, sourceChannels, targetChannels);
        }

        if (sourceRate != targetRate)
        {
            samples = Resample(samples, targetChannels, sourceRate, targetRate);
        }

        return samples;
    }

    private static float[] ConvertChannels(float[] samples, int sourceChannels, int targetChannels)
    {
        var frames = samples.Length / sourceChannels;
        var output = new float[frames * targetChannels];

        for (var frame = 0; frame < frames; frame++)
        {
            for (var ch = 0; ch < targetChannels; ch++)
            {
                // Duplicate the last source channel when upmixing, drop extra channels when downmixing
                var sourceChannel = Math.Min(ch, sourceChannels - 1);
                output[frame * targetChannels + ch] = samples[frame * sourceChannels + sourceChannel];
            }
        }

        return output;
    }

    private static float[] Resample(float[] samples, int channels, int sourceRate, int targetRate)
    {
        var sourceFrames = samples.Length / channels;
        var targetFrames = (int)((long)sourceFrames * targetRate / sourceRate);
        var output = new float[targetFrames * channels];
        var step = (double)sourceRate / targetRate;

        for (var frame = 0; frame < targetFrames; frame++)
        {
            var sourcePosition = frame * step;
            var frame0 = (int)sourcePosition;
            var frame1 = Math.Min(frame0 + 1, sourceFrames - 1);
            var t = (float)(sourcePosition - frame0);

            for (var ch = 0; ch < channels; ch++)
            {
                var s0 = samples[frame0 * channels + ch];
                var s1 = samples[frame1 * channels + ch];
                output[frame * channels + ch] = float.Lerp(s0, s1, t);
            }
        }

        return output;
    }
}

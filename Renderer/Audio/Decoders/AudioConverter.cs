namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Converts decoded audio into the mixer's output format (channel count and sample rate).
/// </summary>
public static class AudioConverter
{
    /// <summary>
    /// Converts interleaved float samples to the target channel count and sample rate (linear
    /// interpolation) and quantizes to interleaved 16-bit PCM, in a single pass with no intermediate
    /// buffers - decoded sounds are large, and full-length temporaries are large object heap garbage
    /// whose collection pauses show up as frame hitches. Quantization applies TPDF dither (triangular,
    /// ±1 LSB) so the error is decorrelated noise rather than distortion, keeping quiet fades and
    /// tails clean; values are rounded and clamped, so inputs slightly outside [-1, 1] (e.g. from MP3
    /// decoding) do not wrap.
    /// </summary>
    public static short[] ConvertToPcm16(float[] samples, int sourceChannels, int sourceRate, int targetChannels, int targetRate)
    {
        var sourceFrames = samples.Length / sourceChannels;
        var targetFrames = sourceRate == targetRate
            ? sourceFrames
            : (int)((long)sourceFrames * targetRate / sourceRate);

        var output = new short[targetFrames * targetChannels];
        var step = (double)sourceRate / targetRate;
        var random = Random.Shared;

        for (var frame = 0; frame < targetFrames; frame++)
        {
            var sourcePosition = frame * step;
            var frame0 = (int)sourcePosition;
            var frame1 = Math.Min(frame0 + 1, sourceFrames - 1);
            var t = (float)(sourcePosition - frame0);

            for (var ch = 0; ch < targetChannels; ch++)
            {
                // Duplicate the last source channel when upmixing, drop extra channels when downmixing
                var sourceChannel = Math.Min(ch, sourceChannels - 1);
                var s0 = samples[frame0 * sourceChannels + sourceChannel];
                var s1 = samples[frame1 * sourceChannels + sourceChannel];
                var value = float.Lerp(s0, s1, t);

                // Difference of two uniform [0,1) draws gives a triangular distribution over (-1, 1) LSB
                var dither = random.NextSingle() - random.NextSingle();
                output[frame * targetChannels + ch] = (short)Math.Clamp(MathF.Round(value * 32767f + dither), short.MinValue, short.MaxValue);
            }
        }

        return output;
    }
}

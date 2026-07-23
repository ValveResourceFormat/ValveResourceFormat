namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Microsoft ADPCM (WAVE format tag 2) decoder.
/// </summary>
public static class MsAdpcmDecoder
{
    private static readonly int[] AdaptationTable =
    [
        230, 230, 230, 230, 307, 409, 512, 614,
        768, 614, 512, 409, 307, 230, 230, 230,
    ];

    private static readonly int[] AdaptCoeff1 = [256, 512, 0, 192, 240, 460, 392];
    private static readonly int[] AdaptCoeff2 = [0, -256, 0, 64, 0, -208, -232];

    /// <summary>Decodes MS ADPCM blocks into interleaved float samples, or null when the parameters are invalid.</summary>
    public static float[]? Decode(byte[] data, int channels, int blockAlign)
    {
        if (channels < 1 || channels > 2 || blockAlign < 7 * channels)
        {
            return null;
        }

        var samplesPerBlock = (blockAlign - 7 * channels) * 2 / channels + 2;
        var blockCount = data.Length / blockAlign;
        var output = new float[blockCount * samplesPerBlock * channels];
        var outputIndex = 0;

        Span<int> coeff1 = stackalloc int[2];
        Span<int> coeff2 = stackalloc int[2];
        Span<int> delta = stackalloc int[2];
        Span<int> sample1 = stackalloc int[2];
        Span<int> sample2 = stackalloc int[2];

        for (var block = 0; block < blockCount; block++)
        {
            var pos = block * blockAlign;
            var blockEnd = pos + blockAlign;

            for (var ch = 0; ch < channels; ch++)
            {
                var predictor = Math.Min((int)data[pos++], 6);
                coeff1[ch] = AdaptCoeff1[predictor];
                coeff2[ch] = AdaptCoeff2[predictor];
            }

            for (var ch = 0; ch < channels; ch++)
            {
                delta[ch] = BitConverter.ToInt16(data, pos);
                pos += 2;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                sample1[ch] = BitConverter.ToInt16(data, pos);
                pos += 2;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                sample2[ch] = BitConverter.ToInt16(data, pos);
                pos += 2;
            }

            // Header samples emitted oldest first
            for (var ch = 0; ch < channels; ch++)
            {
                output[outputIndex++] = sample2[ch] / 32768f;
            }

            for (var ch = 0; ch < channels; ch++)
            {
                output[outputIndex++] = sample1[ch] / 32768f;
            }

            var channel = 0;

            for (; pos < blockEnd && pos < data.Length; pos++)
            {
                var b = data[pos];

                output[outputIndex++] = DecodeNibble(b >> 4, coeff1, coeff2, delta, sample1, sample2, channel) / 32768f;
                channel = (channel + 1) % channels;

                output[outputIndex++] = DecodeNibble(b & 0xF, coeff1, coeff2, delta, sample1, sample2, channel) / 32768f;
                channel = (channel + 1) % channels;
            }
        }

        if (outputIndex < output.Length)
        {
            Array.Resize(ref output, outputIndex);
        }

        return output;
    }

    private static int DecodeNibble(int nibble, Span<int> coeff1, Span<int> coeff2, Span<int> delta, Span<int> sample1, Span<int> sample2, int channel)
    {
        var signed = nibble >= 8 ? nibble - 16 : nibble;

        var predicted = (sample1[channel] * coeff1[channel] + sample2[channel] * coeff2[channel]) / 256 + signed * delta[channel];
        predicted = Math.Clamp(predicted, short.MinValue, short.MaxValue);

        sample2[channel] = sample1[channel];
        sample1[channel] = predicted;

        delta[channel] = Math.Max(AdaptationTable[nibble] * delta[channel] / 256, 16);

        return predicted;
    }
}

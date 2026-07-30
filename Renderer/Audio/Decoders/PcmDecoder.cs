using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Decodes the raw sample data of a WAV-type vsnd (PCM 8/16/24/32 bit or MS ADPCM, described by the
/// sound block's own fields - no container header exists in the stored data, and none is synthesized).
/// Streams fixed-size float chunks to an <see cref="IPcm16Sink"/>, so no whole-file sample buffer ever exists.
/// </summary>
internal static class PcmDecoder
{
    // Scratch chunk size in samples (1 MB of floats); decodes stream through a buffer this size
    internal const int ChunkSamples = 1 << 18;

    /// <summary>Decodes raw little-endian integer PCM, or returns false for an unsupported bit depth.</summary>
    public static bool DecodePcm(ReadOnlySpan<byte> data, int bitsPerSample, int channels, int sampleRate, IPcm16Sink sink)
    {
        if (bitsPerSample is not (8 or 16 or 24 or 32) || channels < 1)
        {
            return false;
        }

        var totalSamples = data.Length / (bitsPerSample / 8);
        sink.SetFormat(channels, sampleRate, totalSamples);

        // Whole frames per chunk so the sink always sees frame-aligned writes
        var chunkSamples = ChunkSamples / channels * channels;
        var scratch = DecodeScratch.RentFloats(chunkSamples);

        for (var start = 0; start < totalSamples; start += chunkSamples)
        {
            var count = Math.Min(chunkSamples, totalSamples - start);

            switch (bitsPerSample)
            {
                case 8:
                    for (var i = 0; i < count; i++)
                    {
                        scratch[i] = (data[start + i] - 128) / 128f;
                    }

                    break;

                case 16:
                    {
                        var samples = MemoryMarshal.Cast<byte, short>(data);
                        for (var i = 0; i < count; i++)
                        {
                            scratch[i] = samples[start + i] / 32768f;
                        }

                        break;
                    }

                case 24:
                    for (var i = 0; i < count; i++)
                    {
                        var index = (start + i) * 3;
                        var value = (data[index] << 8 | data[index + 1] << 16 | data[index + 2] << 24) >> 8;
                        scratch[i] = value / 8388608f;
                    }

                    break;

                case 32:
                    {
                        var samples = MemoryMarshal.Cast<byte, int>(data);
                        for (var i = 0; i < count; i++)
                        {
                            scratch[i] = samples[start + i] / 2147483648f;
                        }

                        break;
                    }
            }

            sink.Write(scratch.AsSpan(0, count));
        }

        return totalSamples > 0;
    }

    /// <summary>
    /// Decodes raw MS ADPCM block data. <paramref name="blockAlign"/> comes from the sound's stored
    /// WAVEFORMATEX header (see <see cref="GetAdpcmBlockAlign"/>).
    /// </summary>
    public static bool DecodeAdpcm(ReadOnlySpan<byte> data, int channels, int blockAlign, int sampleRate, IPcm16Sink sink)
    {
        if (channels is < 1 or > 2 || blockAlign < 7 * channels)
        {
            return false;
        }

        var samplesPerBlock = ((blockAlign - 7 * channels) * 2 / channels + 2) * channels;
        var blockCount = data.Length / blockAlign;
        sink.SetFormat(channels, sampleRate, (long)blockCount * samplesPerBlock);

        // Whole blocks per chunk
        var blocksPerChunk = Math.Max(ChunkSamples / samplesPerBlock, 1);
        var scratch = DecodeScratch.RentFloats(blocksPerChunk * samplesPerBlock);

        for (var block = 0; block < blockCount; block += blocksPerChunk)
        {
            var blocks = Math.Min(blocksPerChunk, blockCount - block);
            var written = 0;

            for (var i = 0; i < blocks; i++)
            {
                written += MsAdpcmDecoder.DecodeBlock(
                    data.Slice((block + i) * blockAlign, blockAlign),
                    channels,
                    scratch.AsSpan(written));
            }

            sink.Write(scratch.AsSpan(0, written));
        }

        return blockCount > 0;
    }

    /// <summary>
    /// Extracts nBlockAlign from a stored ADPCM WAVEFORMATEX header blob
    /// (the sound block's <c>Header</c>), or -1 when the blob is too short.
    /// </summary>
    public static int GetAdpcmBlockAlign(ReadOnlySpan<byte> waveFormatHeader)
        => waveFormatHeader.Length >= 14 ? BinaryPrimitives.ReadUInt16LittleEndian(waveFormatHeader[12..]) : -1;
}

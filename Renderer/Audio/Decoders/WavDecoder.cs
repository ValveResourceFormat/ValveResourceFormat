using System.IO;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// Minimal RIFF/WAVE decoder supporting PCM (8/16/24/32 bit), IEEE float, and MS ADPCM.
/// </summary>
public static class WavDecoder
{
    private const uint RiffMagic = 0x46464952; // "RIFF"
    private const uint WaveMagic = 0x45564157; // "WAVE"
    private const uint FmtMagic = 0x20746D66; // "fmt "
    private const uint DataMagic = 0x61746164; // "data"

    private const ushort FormatPcm = 1;
    private const ushort FormatMsAdpcm = 2;
    private const ushort FormatIeeeFloat = 3;

    /// <summary>Decodes a RIFF/WAVE stream, or returns null when the file or its audio format is not supported.</summary>
    public static DecodedAudio? Decode(Stream stream)
    {
        using var reader = new BinaryReader(stream, System.Text.Encoding.ASCII, leaveOpen: true);

        if (reader.ReadUInt32() != RiffMagic)
        {
            return null;
        }

        reader.ReadUInt32(); // riff size

        if (reader.ReadUInt32() != WaveMagic)
        {
            return null;
        }

        ushort formatTag = 0;
        ushort channels = 0;
        ushort blockAlign = 0;
        ushort bitsPerSample = 0;
        var sampleRate = 0;
        float[]? samples = null;

        while (stream.Position + 8 <= stream.Length)
        {
            var chunkId = reader.ReadUInt32();
            var chunkSize = reader.ReadUInt32();
            var nextChunk = stream.Position + chunkSize + (chunkSize & 1);

            if (chunkId == FmtMagic)
            {
                formatTag = reader.ReadUInt16();
                channels = reader.ReadUInt16();
                sampleRate = reader.ReadInt32();
                reader.ReadInt32(); // average bytes per second
                blockAlign = reader.ReadUInt16();
                bitsPerSample = reader.ReadUInt16();
            }
            else if (chunkId == DataMagic)
            {
                var data = reader.ReadBytes((int)Math.Min(chunkSize, stream.Length - stream.Position));

                samples = formatTag switch
                {
                    FormatPcm => DecodePcm(data, bitsPerSample),
                    FormatIeeeFloat when bitsPerSample == 32 => DecodeFloat(data),
                    FormatMsAdpcm => MsAdpcmDecoder.Decode(data, channels, blockAlign),
                    _ => null,
                };
            }

            stream.Position = nextChunk;
        }

        if (samples == null || channels == 0 || sampleRate <= 0)
        {
            return null;
        }

        return new DecodedAudio
        {
            Samples = samples,
            Channels = channels,
            SampleRate = sampleRate,
        };
    }

    private static float[]? DecodePcm(byte[] data, int bitsPerSample)
    {
        switch (bitsPerSample)
        {
            case 8:
                {
                    var samples = new float[data.Length];
                    for (var i = 0; i < data.Length; i++)
                    {
                        samples[i] = (data[i] - 128) / 128f;
                    }

                    return samples;
                }

            case 16:
                {
                    var samples = new float[data.Length / 2];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        samples[i] = BitConverter.ToInt16(data, i * 2) / 32768f;
                    }

                    return samples;
                }

            case 24:
                {
                    var samples = new float[data.Length / 3];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        var value = (data[i * 3] << 8 | data[i * 3 + 1] << 16 | data[i * 3 + 2] << 24) >> 8;
                        samples[i] = value / 8388608f;
                    }

                    return samples;
                }

            case 32:
                {
                    var samples = new float[data.Length / 4];
                    for (var i = 0; i < samples.Length; i++)
                    {
                        samples[i] = BitConverter.ToInt32(data, i * 4) / 2147483648f;
                    }

                    return samples;
                }

            default:
                return null;
        }
    }

    private static float[] DecodeFloat(byte[] data)
    {
        var samples = new float[data.Length / 4];
        Buffer.BlockCopy(data, 0, samples, 0, samples.Length * 4);
        return samples;
    }
}

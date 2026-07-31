using System.IO;
using NLayer;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// MP3 decoder backed by NLayer (fully managed, no platform audio dependencies). Reads fixed-size chunks
/// straight through to an <see cref="IPcm16Sink"/>, so no whole-file sample buffer ever exists.
/// NLayer applies the Xing/LAME gapless info itself: the leading encoder delay is skipped on the first
/// read, and the trailing padding is excluded from the length it stops at.
/// </summary>
internal static class Mp3Decoder
{
    /// <summary>Decodes raw MP3 data, or returns false when no audio could be parsed.</summary>
    /// <param name="data">Buffer holding the MP3 file.</param>
    /// <param name="dataLength">Length of the MP3 file within <paramref name="data"/>.</param>
    /// <param name="sink">Receives the decoded samples.</param>
    /// <param name="truncated">Set when the decode hit damaged frames and may be missing samples.</param>
    public static bool Decode(byte[] data, int dataLength, IPcm16Sink sink, out bool truncated)
    {
        truncated = false;

        using var stream = new MemoryStream(data, 0, dataLength, writable: false);
        using var mpeg = new MpegFile(stream);

        var channels = mpeg.Channels;

        if (channels < 1 || mpeg.SampleRate <= 0)
        {
            return false;
        }

        // Length counts bytes of float samples, with the gapless trim already applied. Only a sizing
        // hint - Pcm16ArenaSink prefers the sample count the vsnd resource itself reports.
        sink.SetFormat(channels, mpeg.SampleRate, mpeg.Length / sizeof(float));

        // Whole frames per chunk so the sink always sees frame-aligned writes
        var chunkSamples = PcmDecoder.ChunkSamples / channels * channels;
        var scratch = DecodeScratch.RentFloats(chunkSamples);
        var written = 0L;
        var failures = 0;

        while (failures < 16)
        {
            int read;

            try
            {
                read = mpeg.ReadSamples(scratch, 0, chunkSamples);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
            {
                // Retry past the damaged frame so one bad spot does not lose the rest of the file,
                // capped for data damaged badly enough that the reader never gets back in sync
                failures++;
                continue;
            }

            if (read <= 0)
            {
                break;
            }

            sink.Write(scratch.AsSpan(0, read));
            written += read;
        }

        truncated = failures > 0;
        return written > 0;
    }
}

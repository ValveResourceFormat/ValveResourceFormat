using System.IO;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// MP3 decoder backed by NLayer (fully managed, no platform audio dependencies).
/// </summary>
public static class Mp3Decoder
{
    /// <summary>Decodes an MP3 stream, or returns null when it cannot be parsed.</summary>
    public static DecodedAudio? Decode(Stream stream)
    {
        using var mpeg = new NLayer.MpegFile(stream);

        if (mpeg.Channels < 1 || mpeg.SampleRate <= 0)
        {
            return null;
        }

        var samples = new List<float>(65536);
        var buffer = new float[16384];
        var failures = 0;

        // Retry on decoder errors so a single bad frame does not lose the rest of the
        // file, with a cap for streams where the reader cannot advance past the damage.
        while (failures < 16)
        {
            int read;

            try
            {
                read = mpeg.ReadSamples(buffer, 0, buffer.Length);
            }
            catch (Exception e) when (e is IndexOutOfRangeException or ArgumentException or InvalidDataException)
            {
                failures++;
                continue;
            }

            if (read <= 0)
            {
                break;
            }

            samples.AddRange(buffer.AsSpan(0, read));
        }

        if (samples.Count == 0)
        {
            return null;
        }

        return new DecodedAudio
        {
            Samples = [.. samples],
            Channels = mpeg.Channels,
            SampleRate = mpeg.SampleRate,
            // A decode that hit bad frames may be truncated; loop points must not be trusted
            Truncated = failures > 0,
        };
    }
}

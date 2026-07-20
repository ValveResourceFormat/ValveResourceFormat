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

        // Decode directly into an array sized from the reported duration. Long ambients are tens of
        // megabytes of samples, so list-style growth doubling would repeatedly reallocate and copy on
        // the large object heap - that garbage collector pressure shows up as frame hitches.
        var estimatedSamples = (long)(mpeg.Duration.TotalSeconds * mpeg.SampleRate) * mpeg.Channels;
        var samples = new float[Math.Clamp(estimatedSamples, 65536, int.MaxValue - 1)];
        var count = 0;
        var failures = 0;

        // Retry on decoder errors so a single bad frame does not lose the rest of the
        // file, with a cap for streams where the reader cannot advance past the damage.
        while (failures < 16)
        {
            if (count == samples.Length)
            {
                // The duration estimate came up short (e.g. a header-less stream): grow by half
                Array.Resize(ref samples, samples.Length + samples.Length / 2);
            }

            int read;

            try
            {
                read = mpeg.ReadSamples(samples, count, samples.Length - count);
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

            count += read;
        }

        if (count == 0)
        {
            return null;
        }

        if (count != samples.Length)
        {
            Array.Resize(ref samples, count);
        }

        return new DecodedAudio
        {
            Samples = samples,
            Channels = mpeg.Channels,
            SampleRate = mpeg.SampleRate,
            // A decode that hit bad frames may be truncated; loop points must not be trusted
            Truncated = failures > 0,
        };
    }
}

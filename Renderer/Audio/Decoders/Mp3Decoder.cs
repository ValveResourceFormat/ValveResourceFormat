using System.IO;

namespace ValveResourceFormat.Renderer.Audio.Decoders;

/// <summary>
/// MP3 decoder backed by NLayer (fully managed, no platform audio dependencies).
/// </summary>
public static class Mp3Decoder
{
    public static DecodedAudio? Decode(Stream stream)
    {
        using var mpeg = new NLayer.MpegFile(stream);

        if (mpeg.Channels < 1 || mpeg.SampleRate <= 0)
        {
            return null;
        }

        var samples = new List<float>(65536);
        var buffer = new float[16384];

        int read;
        while ((read = mpeg.ReadSamples(buffer, 0, buffer.Length)) > 0)
        {
            samples.AddRange(buffer.AsSpan(0, read));
        }

        return new DecodedAudio
        {
            Samples = [.. samples],
            Channels = mpeg.Channels,
            SampleRate = mpeg.SampleRate,
        };
    }
}

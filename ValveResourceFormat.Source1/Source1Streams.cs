using System.IO;

namespace ValveResourceFormat.Source1;

internal static class Source1Streams
{
    /// <summary>Reads the rest of a stream into an array.</summary>
    public static byte[] ReadAll(Stream stream)
    {
        ArgumentNullException.ThrowIfNull(stream);

        if (!stream.CanSeek)
        {
            using var buffer = new MemoryStream();
            stream.CopyTo(buffer);
            return buffer.ToArray();
        }

        var data = new byte[stream.Length - stream.Position];
        stream.ReadExactly(data);
        return data;
    }
}

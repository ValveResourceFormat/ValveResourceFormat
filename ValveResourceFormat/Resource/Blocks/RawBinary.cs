using System.IO;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// An opaque block of raw binary data.
/// </summary>
public abstract class RawBinary : Block
{
    /// <inheritdoc/>
    public override void Read(BinaryReader reader)
    {
        // Binary data is not read into memory upon load
    }

    /// <inheritdoc/>
    public override void Serialize(Stream stream)
    {
        if (Resource?.Reader == null)
        {
            throw new NotImplementedException("Serializing this block currently only works when modifying an existing resource.");
        }

        // The dumbest implementation.
        var data = new byte[Size];
        Resource.Reader.BaseStream.Position = Offset;
        Resource.Reader.Read(data);
        stream.Write(data);
    }

    /// <inheritdoc/>
    public override void WriteText(IndentedTextWriter writer)
    {
        ArgumentNullException.ThrowIfNull(Resource?.Reader);

        var data = new byte[Size];
        Resource.Reader.BaseStream.Position = Offset;
        Resource.Reader.Read(data);

        for (var i = 0; i < data.Length; i += 16)
        {
            var lineLength = Math.Min(16, data.Length - i);

            for (var j = 0; j < lineLength; j++)
            {
                if (j > 0)
                {
                    writer.Write(' ');
                }

                writer.Write(data[i + j].ToString("X2", System.Globalization.CultureInfo.InvariantCulture));
            }

            writer.WriteLine();
        }
    }
}

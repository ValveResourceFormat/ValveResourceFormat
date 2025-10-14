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
        writer.WriteLine("Not yet.");
    }
}

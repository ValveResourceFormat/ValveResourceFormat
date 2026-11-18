using System.IO;
using System.Text;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// "PCRC" block. Panorama images referenced by a compiled vdata file.
/// </summary>
public class PanoramaImageReferences : Block
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.PCRC;

    private const byte Version = 1;

    /// <summary>
    /// Gets the referenced images with their original dimensions and source file CRC32.
    /// </summary>
    public List<Panorama.ImageEntry> Images { get; } = [];

    /// <inheritdoc/>
    public override void Read(BinaryReader reader)
    {
        reader.BaseStream.Position = Offset;

        var version = reader.ReadByte();

        if (version != Version)
        {
            throw new UnexpectedMagicException("Unknown PCRC version", version);
        }

        var imageCount = reader.ReadUInt16();
        Images.EnsureCapacity(imageCount);

        for (var i = 0; i < imageCount; i++)
        {
            Images.Add(Panorama.ImageEntry.Read(reader, hasCRC32: true));
        }
    }

    /// <inheritdoc/>
    public override void Serialize(Stream stream)
    {
        using var writer = new BinaryWriter(stream, Encoding.UTF8, leaveOpen: true);

        writer.Write(Version);
        writer.Write((ushort)Images.Count);

        foreach (var image in Images)
        {
            image.Write(writer, hasCRC32: true);
        }
    }

    /// <inheritdoc/>
    public override void WriteText(IndentedTextWriter writer)
    {
        writer.WriteLine($"Images({Images.Count}):");

        foreach (var image in Images)
        {
            writer.WriteLine($" - {image.Name} [{image.Width}x{image.Height} - {image.CRC32:X8}]");
        }
    }
}

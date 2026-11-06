using System.Buffers.Binary;
using System.IO;
using ValveResourceFormat.Source1;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.GoldSrc;

/// <summary>
/// A WAD3 texture archive.
/// </summary>
internal sealed class WadFile
{
    private const int Identifier = 0x33444157;
    private const int HeaderSize = 12;
    private const int EntrySize = 32;

    private readonly byte[] data;
    private readonly Dictionary<string, int> lumpsByName = new(StringComparer.OrdinalIgnoreCase);

    public string Name { get; }
    public IReadOnlyList<WadLump> Lumps { get; }

    public WadFile(byte[] data, string name)
    {
        this.data = data;
        Name = name;

        if (data.Length < HeaderSize)
        {
            throw new InvalidDataException("File is too small to be a WAD3 file.");
        }

        var identifier = BinaryPrimitives.ReadInt32LittleEndian(data);

        if (identifier != Identifier)
        {
            throw new UnexpectedMagicException("Not a WAD3 file", identifier, nameof(identifier));
        }

        var count = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(4));
        var tableOffset = BinaryPrimitives.ReadInt32LittleEndian(data.AsSpan(8));

        if (count < 0 || tableOffset < HeaderSize || tableOffset > data.Length - ((long)count * EntrySize))
        {
            throw new InvalidDataException("WAD3 lump table is out of range.");
        }

        var lumps = new WadLump[count];

        for (var i = 0; i < count; i++)
        {
            var entry = data.AsSpan(tableOffset + (i * EntrySize), EntrySize);
            var offset = BinaryPrimitives.ReadInt32LittleEndian(entry);
            var diskSize = BinaryPrimitives.ReadInt32LittleEndian(entry[4..]);
            var size = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
            var type = (WadLumpType)entry[12];
            var compression = entry[13];
            var lumpName = PalettedImage.ReadName(entry[16..]);

            lumps[i] = new WadLump(lumpName, type, offset, diskSize, size, compression);
            lumpsByName.TryAdd(lumpName, i);
        }

        Lumps = lumps;
    }

    /// <summary>
    /// Whether the file starts like a WAD3.
    /// </summary>
    public static bool IsWad(ReadOnlySpan<byte> header) => header.Length >= 4 && BinaryPrimitives.ReadInt32LittleEndian(header) == Identifier;

    public static WadFile Open(Stream stream, string name) => new(Source1Streams.ReadAll(stream), name);

    public static WadFile Open(string path)
    {
        return new WadFile(File.ReadAllBytes(path), Path.GetFileNameWithoutExtension(path));
    }

    public ReadOnlySpan<byte> GetLumpData(WadLump lump)
    {
        if (lump.Offset < 0 || lump.DiskSize < 0 || lump.Offset > data.Length - lump.DiskSize)
        {
            throw new InvalidDataException($"WAD3 lump \"{lump.Name}\" is out of range.");
        }

        return data.AsSpan(lump.Offset, lump.DiskSize);
    }

    /// <summary>
    /// Reads a lump as an image. Returns null for lumps of an unknown type or for miptex lumps without pixel data.
    /// </summary>
    public PalettedImage? ReadImage(WadLump lump)
    {
        if (lump.Compression != 0)
        {
            throw new NotSupportedException($"WAD3 lump \"{lump.Name}\" uses compression {lump.Compression}.");
        }

        var lumpData = GetLumpData(lump);

        return lump.Type switch
        {
            WadLumpType.MipTexture or WadLumpType.TempDecal => PalettedImage.ReadMipTexture(lumpData),
            WadLumpType.Qpic => PalettedImage.ReadQpic(lump.Name, lumpData),
            WadLumpType.Font => PalettedImage.ReadFont(lump.Name, lumpData),
            _ => null,
        };
    }

    /// <summary>
    /// Finds a texture by name, ignoring case.
    /// </summary>
    public PalettedImage? FindImage(string textureName)
    {
        return lumpsByName.TryGetValue(textureName, out var index) ? ReadImage(Lumps[index]) : null;
    }
}

/// <summary>An entry of the WAD3 lump table.</summary>
internal readonly record struct WadLump(string Name, WadLumpType Type, int Offset, int DiskSize, int Size, byte Compression);

/// <summary>Lump types of a WAD3 file.</summary>
internal enum WadLumpType : byte
{
    TempDecal = 0x40,
    Qpic = 0x42,
    MipTexture = 0x43,
    Font = 0x46,
}

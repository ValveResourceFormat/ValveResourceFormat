using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

[Flags]
internal enum StaticPropFlags : uint
{
    Fades = 1,
    UseLightingOrigin = 2,
    NoFlashlight = 4,
    IgnoreNormals = 8,
    NoShadow = 0x10,
    Unused = 0x20,
    NoPerVertexLighting = 0x40,
    NoSelfShadowing = 0x80,
    NoPerTexelLighting = 0x100,
}

[Flags]
internal enum StaticPropExtendedFlags : uint
{
    None = 0,

    /// <summary>The prop casts no real-time shadows, though it still shadows the lightmaps.</summary>
    DisableShadowDepth = 1,
}

internal sealed class BspStaticProp
{
    public string ModelName { get; init; } = string.Empty;
    public int PropType { get; init; }
    public Vector3 Origin { get; init; }
    public Vector3 Angles { get; init; }
    public ushort FirstLeaf { get; init; }
    public ushort LeafCount { get; init; }
    public byte Solid { get; init; }
    public StaticPropFlags Flags { get; init; }
    public StaticPropExtendedFlags ExtendedFlags { get; init; }
    public int Skin { get; init; }
    public float FadeMinDist { get; init; }
    public float FadeMaxDist { get; init; }
    public Vector3 LightingOrigin { get; init; }
    public float ForcedFadeScale { get; init; } = 1f;
    public uint ColorModulation { get; init; } = 0xffffffff;
    public Vector3 Scale { get; init; } = Vector3.One;
}

internal sealed class BspStaticProps
{
    private const int MinEntrySize = 56;
    private const int MinLumpVersion = 4;
    private const int MaxLumpVersion = 13;
    private const int WideLeafLumpVersion = 12;
    private const int ScaleVectorLumpVersion = 13;
    private const int ScaleVectorEntrySize = 88;

    private readonly bool hasWideFlags;
    private readonly bool hasCombinedModels;

    public int LumpVersion { get; }
    public int EntrySize { get; }
    public IReadOnlyList<string> ModelDictionary { get; } = [];
    public IReadOnlyList<int> LeafDictionary { get; } = [];
    public IReadOnlyList<BspStaticProp> Props { get; } = [];

    internal BspStaticProps(BspFile bsp)
    {
        var gameLump = bsp.GameLump;
        var data = gameLump.GetItemData("sprp");

        if (data == null)
        {
            return;
        }

        LumpVersion = gameLump.GetItemVersion("sprp");

        if (LumpVersion < MinLumpVersion || LumpVersion > MaxLumpVersion)
        {
            throw new UnexpectedMagicException($"Unsupported static prop lump version, supported versions are {MinLumpVersion} to {MaxLumpVersion}", LumpVersion, nameof(LumpVersion));
        }

        var span = data.AsSpan();
        var position = 0;

        var models = BspGameLump.ReadNameTable(span, ref position);

        ModelDictionary = models;

        // Some maps put a second name table before the leaf list, and their entries end with an index into it
        // instead of a color modulation
        var leafSize = LumpVersion >= WideLeafLumpVersion ? sizeof(int) : sizeof(ushort);

        if (!IsLeafListLayout(span, position, leafSize) && TryGetEndOfNameTable(span, position, out var afterTable) && IsLeafListLayout(span, afterTable, leafSize))
        {
            position = afterTable;
            hasCombinedModels = true;
        }

        var leafCount = ReadCount(span, ref position, leafSize);
        var leaves = new int[leafCount];

        for (var i = 0; i < leafCount; i++)
        {
            leaves[i] = leafSize == sizeof(int)
                ? BinaryPrimitives.ReadInt32LittleEndian(span[position..])
                : BinaryPrimitives.ReadUInt16LittleEndian(span[position..]);
            position += leafSize;
        }

        LeafDictionary = leaves;

        var propCount = ReadCount(span, ref position, 1);

        if (propCount == 0)
        {
            return;
        }

        EntrySize = (span.Length - position) / propCount;

        if (EntrySize < MinEntrySize)
        {
            throw new InvalidDataException("Static prop entries are too short.");
        }

        hasWideFlags = HasWideFlags(span[position..], propCount);

        var props = new BspStaticProp[propCount];

        for (var i = 0; i < propCount; i++)
        {
            var entry = span.Slice(position + (i * EntrySize), EntrySize);
            props[i] = ReadProp(entry, models);
        }

        Props = props;
    }

    // Entries of 72 bytes in versions 7 and 10 move the flags to a 32-bit field at 64. Entries of 80 bytes in
    // version 11 either end with a float scale at 76, or use the same wide flags when that field is not a float scale.
    private bool HasWideFlags(ReadOnlySpan<byte> entries, int propCount)
    {
        if (LumpVersion == 7 || LumpVersion == 10)
        {
            return EntrySize == 72;
        }

        if (LumpVersion < 11 || EntrySize < 80)
        {
            return false;
        }

        for (var i = 0; i < propCount; i++)
        {
            var scale = BinaryPrimitives.ReadSingleLittleEndian(entries[(i * EntrySize + 76)..]);

            if (!float.IsNormal(scale) || scale <= 0f)
            {
                return true;
            }
        }

        return false;
    }

    private static bool TryGetEndOfNameTable(ReadOnlySpan<byte> span, int position, out int end)
    {
        end = position;

        if (!TryPeekCount(span, position, BspGameLump.NameSize, out var count))
        {
            return false;
        }

        end = position + sizeof(int) + (count * BspGameLump.NameSize);
        return true;
    }

    // Reads the count at position and checks that the elements that follow it fit in the span
    private static bool TryPeekCount(ReadOnlySpan<byte> span, int position, int elementSize, out int count)
    {
        count = 0;

        if (position + sizeof(int) > span.Length)
        {
            return false;
        }

        count = BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
        return count >= 0 && (long)count * elementSize <= span.Length - position - sizeof(int);
    }

    // The leaf list and the props must fill the rest of the lump exactly
    private static bool IsLeafListLayout(ReadOnlySpan<byte> span, int position, int leafSize)
    {
        if (!TryPeekCount(span, position, leafSize, out var leafCount))
        {
            return false;
        }

        position += sizeof(int) + (leafCount * leafSize);

        if (position + sizeof(int) > span.Length)
        {
            return false;
        }

        var propCount = BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
        position += sizeof(int);

        var remaining = span.Length - position;

        if (propCount == 0)
        {
            return remaining == 0;
        }

        return propCount > 0 && remaining % propCount == 0 && remaining / propCount >= MinEntrySize;
    }

    private static int ReadCount(ReadOnlySpan<byte> span, ref int position, int elementSize)
    {
        var count = BinaryPrimitives.ReadInt32LittleEndian(span[position..]);
        position += sizeof(int);

        if (count < 0 || (long)count * elementSize > span.Length - position)
        {
            throw new InvalidDataException("Static prop lump is truncated.");
        }

        return count;
    }

    private Vector3 ReadScale(ReadOnlySpan<byte> entry)
    {
        if (LumpVersion >= ScaleVectorLumpVersion && entry.Length >= ScaleVectorEntrySize)
        {
            return MemoryMarshal.Read<Vector3>(entry[76..]);
        }

        return new Vector3(BinaryPrimitives.ReadSingleLittleEndian(entry[76..]));
    }

    private BspStaticProp ReadProp(ReadOnlySpan<byte> entry, string[] models)
    {
        var propType = BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]);
        var hasForcedFadeScale = LumpVersion >= 5 && entry.Length >= 60;
        var hasExtendedFlags = LumpVersion >= 10 && entry.Length >= 76 && !hasWideFlags;
        var hasScale = LumpVersion >= 11 && entry.Length >= 80 && !hasWideFlags;
        var flags = hasWideFlags ? BinaryPrimitives.ReadUInt32LittleEndian(entry[64..]) : entry[31];

        // Wide flags are followed by the size of the prop lightmap and then the color modulation, if the entry has room
        var colorOffset = hasWideFlags ? 72 : 64;
        var hasColorModulation = LumpVersion >= 7 && entry.Length >= colorOffset + sizeof(uint) && !hasCombinedModels;

        return new BspStaticProp
        {
            ModelName = propType < models.Length ? models[propType] : string.Empty,
            PropType = propType,
            Origin = MemoryMarshal.Read<Vector3>(entry),
            Angles = MemoryMarshal.Read<Vector3>(entry[12..]),
            FirstLeaf = BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]),
            LeafCount = BinaryPrimitives.ReadUInt16LittleEndian(entry[28..]),
            Solid = entry[30],
            Flags = (StaticPropFlags)flags,
            ExtendedFlags = hasExtendedFlags ? (StaticPropExtendedFlags)BinaryPrimitives.ReadUInt32LittleEndian(entry[72..]) : StaticPropExtendedFlags.None,
            Skin = BinaryPrimitives.ReadInt32LittleEndian(entry[32..]),
            FadeMinDist = BinaryPrimitives.ReadSingleLittleEndian(entry[36..]),
            FadeMaxDist = BinaryPrimitives.ReadSingleLittleEndian(entry[40..]),
            LightingOrigin = MemoryMarshal.Read<Vector3>(entry[44..]),
            ForcedFadeScale = hasForcedFadeScale ? BinaryPrimitives.ReadSingleLittleEndian(entry[56..]) : 1f,
            ColorModulation = hasColorModulation ? BinaryPrimitives.ReadUInt32LittleEndian(entry[colorOffset..]) : 0xffffffff,
            Scale = hasScale ? ReadScale(entry) : Vector3.One,
        };
    }
}

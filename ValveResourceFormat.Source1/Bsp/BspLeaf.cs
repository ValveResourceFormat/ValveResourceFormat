using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>A BSP leaf. Leaves of lump version 0 in v19 and later maps carry an ambient cube that is not read here.</summary>
internal readonly record struct BspLeaf(
    int Contents,
    short Cluster,
    short Area,
    short Flags,
    Vector3 Mins,
    Vector3 Maxs,
    int FirstLeafFace,
    int NumLeafFaces,
    int FirstLeafBrush,
    int NumLeafBrushes,
    short LeafWaterDataId)
{
    private const int Version1Size = 32;
    private const int Version0Size = 56;

    internal static BspLeaf[] Read(bool hasAmbientCube, ReadOnlySpan<byte> data)
    {
        var size = hasAmbientCube ? Version0Size : Version1Size;
        var count = data.Length / size;
        var leafs = new BspLeaf[count];

        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(i * size, size);
            var areaAndFlags = BinaryPrimitives.ReadInt16LittleEndian(entry[6..]);

            leafs[i] = new BspLeaf(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt16LittleEndian(entry[4..]),
                (short)(areaAndFlags & 0x1ff),
                (short)((areaAndFlags >> 9) & 0x7f),
                new Vector3(
                    BinaryPrimitives.ReadInt16LittleEndian(entry[8..]),
                    BinaryPrimitives.ReadInt16LittleEndian(entry[10..]),
                    BinaryPrimitives.ReadInt16LittleEndian(entry[12..])),
                new Vector3(
                    BinaryPrimitives.ReadInt16LittleEndian(entry[14..]),
                    BinaryPrimitives.ReadInt16LittleEndian(entry[16..]),
                    BinaryPrimitives.ReadInt16LittleEndian(entry[18..])),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[20..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[22..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]),
                BinaryPrimitives.ReadInt16LittleEndian(entry[28..]));
        }

        return leafs;
    }

    /// <summary>Reads the leaves of maps with 32-bit indices, which store the bounds as floats and the area and flags as separate fields.</summary>
    internal static BspLeaf[] ReadWide(ReadOnlySpan<byte> data)
    {
        var count = data.Length / Version0Size;
        var leafs = new BspLeaf[count];

        for (var i = 0; i < count; i++)
        {
            var entry = data.Slice(i * Version0Size, Version0Size);

            leafs[i] = new BspLeaf(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BspWide.ToInt16(BinaryPrimitives.ReadInt32LittleEndian(entry[4..])),
                BinaryPrimitives.ReadInt16LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt16LittleEndian(entry[10..]),
                MemoryMarshal.Read<Vector3>(entry[12..]),
                MemoryMarshal.Read<Vector3>(entry[24..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[36..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[40..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[44..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[48..]),
                BspWide.ToInt16(BinaryPrimitives.ReadInt32LittleEndian(entry[52..])));
        }

        return leafs;
    }
}

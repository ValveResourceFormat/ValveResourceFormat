using System.Buffers.Binary;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Source1.Bsp
{
    /// <summary>A BSP leaf. Lump version 0 leaves carry an ambient cube that is not read here.</summary>
    internal readonly record struct BspLeaf(
        int Contents,
        short Cluster,
        short Area,
        short Flags,
        Vector3 Mins,
        Vector3 Maxs,
        ushort FirstLeafFace,
        ushort NumLeafFaces,
        ushort FirstLeafBrush,
        ushort NumLeafBrushes,
        short LeafWaterDataId)
    {
        private const int Version1Size = 32;
        private const int Version0Size = 56;

        internal static BspLeaf[] Read(int version, ReadOnlySpan<byte> data)
        {
            var size = version == 0 ? Version0Size : Version1Size;
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
    }

    /// <summary>The water surface height of a water leaf (LUMP_LEAFWATERDATA).</summary>
    [StructLayout(LayoutKind.Sequential, Pack = 1, Size = 12)]
    internal readonly struct BspLeafWaterData
    {
        public readonly float SurfaceZ;
        public readonly float MinZ;
        public readonly short SurfaceTexInfoId;
    }
}

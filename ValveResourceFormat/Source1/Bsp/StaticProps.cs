using System.Buffers.Binary;
using System.IO;
using System.Runtime.InteropServices;
using System.Text;

namespace ValveResourceFormat.Source1.Bsp
{
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
        public int Skin { get; init; }
        public float FadeMinDist { get; init; }
        public float FadeMaxDist { get; init; }
        public Vector3 LightingOrigin { get; init; }
        public float ForcedFadeScale { get; init; } = 1f;
        public uint ColorModulation { get; init; } = 0xffffffff;
        public float Scale { get; init; } = 1f;
    }

    internal sealed class BspStaticProps
    {
        private const int ModelNameSize = 128;

        public int LumpVersion { get; }
        public int EntrySize { get; }
        public IReadOnlyList<string> ModelDictionary { get; } = [];
        public IReadOnlyList<ushort> LeafDictionary { get; } = [];
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

            var span = data.AsSpan();
            var position = 0;

            var modelCount = ReadCount(span, ref position, ModelNameSize);
            var models = new string[modelCount];

            for (var i = 0; i < modelCount; i++)
            {
                var nameBytes = span.Slice(position, ModelNameSize);
                var length = nameBytes.IndexOf((byte)0);
                models[i] = Encoding.ASCII.GetString(length < 0 ? nameBytes : nameBytes[..length]);
                position += ModelNameSize;
            }

            ModelDictionary = models;

            var leafCount = ReadCount(span, ref position, sizeof(ushort));
            var leaves = new ushort[leafCount];

            for (var i = 0; i < leafCount; i++)
            {
                leaves[i] = BinaryPrimitives.ReadUInt16LittleEndian(span[position..]);
                position += sizeof(ushort);
            }

            LeafDictionary = leaves;

            var propCount = ReadCount(span, ref position, 1);

            if (propCount == 0)
            {
                return;
            }

            EntrySize = (span.Length - position) / propCount;

            var props = new BspStaticProp[propCount];

            for (var i = 0; i < propCount; i++)
            {
                var entry = span.Slice(position + (i * EntrySize), EntrySize);
                props[i] = ReadProp(entry, models);
            }

            Props = props;
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

        private BspStaticProp ReadProp(ReadOnlySpan<byte> entry, string[] models)
        {
            var propType = BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]);
            var hasForcedFadeScale = LumpVersion >= 5 && entry.Length >= 60;
            var hasScale = LumpVersion >= 11 && entry.Length >= 80;

            // Version 10 entries of 72 bytes move the flags to a 32-bit field at 64, followed by the size of the
            // prop lightmap, and have no color modulation
            var hasWideFlags = LumpVersion == 10 && entry.Length == 72;
            var hasColorModulation = LumpVersion >= 7 && entry.Length >= 68 && !hasWideFlags;
            var flags = hasWideFlags ? BinaryPrimitives.ReadUInt32LittleEndian(entry[64..]) : entry[31];

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
                Skin = BinaryPrimitives.ReadInt32LittleEndian(entry[32..]),
                FadeMinDist = BinaryPrimitives.ReadSingleLittleEndian(entry[36..]),
                FadeMaxDist = BinaryPrimitives.ReadSingleLittleEndian(entry[40..]),
                LightingOrigin = MemoryMarshal.Read<Vector3>(entry[44..]),
                ForcedFadeScale = hasForcedFadeScale ? BinaryPrimitives.ReadSingleLittleEndian(entry[56..]) : 1f,
                ColorModulation = hasColorModulation ? BinaryPrimitives.ReadUInt32LittleEndian(entry[64..]) : 0xffffffff,
                Scale = hasScale ? BinaryPrimitives.ReadSingleLittleEndian(entry[76..]) : 1f,
            };
        }
    }
}

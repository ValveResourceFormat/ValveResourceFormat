using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Source1.Bsp
{
    internal sealed class BspGameLump
    {
        private const int ItemSize = 16;

        private readonly record struct Item(ushort Flags, ushort Version, int FileOffset, int FileLength);

        private readonly BspFile bsp;
        private readonly Dictionary<string, Item> items = [];

        public IReadOnlyCollection<string> ItemIds => items.Keys;

        internal BspGameLump(BspFile bsp)
        {
            this.bsp = bsp;

            var lump = bsp.GetLumpData(LumpType.GameLump).Span;

            if (lump.Length < 4)
            {
                return;
            }

            var count = BinaryPrimitives.ReadInt32LittleEndian(lump);
            count = Math.Min(count, (lump.Length - 4) / ItemSize);

            if (count <= 0)
            {
                return;
            }

            var raw = new (int Id, Item Item)[count];

            for (var i = 0; i < count; i++)
            {
                var entry = lump.Slice(4 + (i * ItemSize), ItemSize);
                var id = BinaryPrimitives.ReadInt32LittleEndian(entry);
                var flags = BinaryPrimitives.ReadUInt16LittleEndian(entry[4..]);
                var version = BinaryPrimitives.ReadUInt16LittleEndian(entry[6..]);
                var offset = BinaryPrimitives.ReadInt32LittleEndian(entry[8..]);
                var length = BinaryPrimitives.ReadInt32LittleEndian(entry[12..]);
                raw[i] = (id, new Item(flags, version, offset, length));
            }

            var lumpInfo = bsp.GetLumpInfo(LumpType.GameLump);
            var lumpOffset = lumpInfo.Offset;
            var lumpEnd = lumpInfo.Offset + lumpInfo.Length;

            for (var i = 0; i < count; i++)
            {
                var (id, item) = raw[i];

                if (id == 0)
                {
                    break;
                }

                const int CompressedFlag = 1;

                if ((item.Flags & CompressedFlag) != 0)
                {
                    var end = lumpEnd;

                    if (i + 1 < count && raw[i + 1].Item.FileOffset > item.FileOffset)
                    {
                        end = raw[i + 1].Item.FileOffset;
                    }

                    item = item with { FileLength = end - item.FileOffset };
                }

                if (!IsInsideFile(item) && item.FileOffset >= 0)
                {
                    var relative = item with { FileOffset = item.FileOffset + lumpOffset };

                    if (IsInsideFile(relative))
                    {
                        item = relative;
                    }
                }

                items[GetIdString(id)] = item;
            }
        }

        private bool IsInsideFile(Item item)
        {
            return item.FileOffset >= 0 && item.FileLength >= 0 && (long)item.FileOffset + item.FileLength <= bsp.FileLength;
        }

        private static string GetIdString(int id)
        {
            Span<char> chars = stackalloc char[4];
            var length = 0;

            for (var shift = 24; shift >= 0; shift -= 8)
            {
                var value = (id >> shift) & 0x7f;

                if (value != 0)
                {
                    chars[length++] = (char)value;
                }
            }

            return new string(chars[..length]);
        }

        public bool HasItem(string id) => items.ContainsKey(id);

        public ushort GetItemVersion(string id) => items[id].Version;

        public ushort GetItemFlags(string id) => items[id].Flags;

        public byte[]? GetItemData(string id)
        {
            if (!items.TryGetValue(id, out var item))
            {
                return null;
            }

            if (!IsInsideFile(item))
            {
                throw new InvalidDataException($"Game lump item '{id}' lies outside the file.");
            }

            var raw = bsp.GetFileData(item.FileOffset, item.FileLength);

            if (BspFile.IsLzmaCompressed(raw.Span))
            {
                return BspFile.DecompressLzma(raw.Span);
            }

            return raw.ToArray();
        }
    }
}

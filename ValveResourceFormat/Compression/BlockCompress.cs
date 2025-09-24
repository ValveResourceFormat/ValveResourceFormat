using System.IO;

namespace ValveResourceFormat.Compression
{
    public static class BlockCompress
    {
        public record struct CompressionInfo(bool IsCompressed, int Size);

        public static CompressionInfo GetDecompressedSize(BinaryReader reader)
        {
            var decompressedSize = reader.ReadUInt32();

            // Valve sets fourth byte in the compressed buffer to 0x80 to indicate that the data is uncompressed,
            // 0x80000000 is 2147483648 which automatically makes any number higher than max signed 32-bit integer.
            if (decompressedSize > int.MaxValue)
            {
                return new(false, (int)(decompressedSize & 0x7FFFFFFF));
            }

            return new(true, (int)decompressedSize);
        }

        public static void FastDecompress(CompressionInfo info, BinaryReader reader, Span<byte> result)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, info.Size);

            if (!info.IsCompressed)
            {
                reader.Read(result[0..info.Size]);
                return;
            }

            var position = 0;
            ushort blockMask = 0;
            var i = 0;

            while (position < info.Size)
            {
                if (i == 0)
                {
                    blockMask = reader.ReadUInt16();
                    i = 16;
                }

                if ((blockMask & 1) > 0)
                {
                    var offsetSize = reader.ReadUInt16();
                    var offset = (offsetSize >> 4) + 1;
                    var size = (offsetSize & 0xF) + 3;
                    var positionSource = position - offset;

                    if (offset == 1)
                    {
                        while (size-- > 0)
                        {
                            result[position++] = result[positionSource];
                        }
                    }
                    else
                    {
                        while (size-- > 0)
                        {
                            result[position++] = result[positionSource++];
                        }
                    }
                }
                else
                {
                    result[position++] = reader.ReadByte();
                }

                blockMask >>= 1;
                i--;
            }
        }
    }
}

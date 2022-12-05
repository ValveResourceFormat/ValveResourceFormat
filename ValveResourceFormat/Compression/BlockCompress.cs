using System;
using System.IO;

namespace ValveResourceFormat.Compression
{
    public static class BlockCompress
    {
        public static ReadOnlySpan<byte> FastDecompress(BinaryReader reader)
        {
            var decompressedSize = reader.ReadUInt32();

            // Valve sets fourth byte in the compressed buffer to 0x80 to indicate that the data is uncompressed,
            // 0x80000000 is 2147483648 which automatically makes any number higher than max signed 32-bit integer.
            if (decompressedSize > int.MaxValue)
            {
                return reader.ReadBytes((int)decompressedSize & 0x7FFFFFFF);
            }

            var result = new Span<byte>(new byte[decompressedSize]);
            var position = 0;
            ushort blockMask = 0;
            var i = 0;

            while (position < decompressedSize)
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
                        // This path is seemingly useless, because it produces equal results.
                        // Is this draw of the luck because `result` is initialized to zeroes?
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

            return result;
        }
    }
}

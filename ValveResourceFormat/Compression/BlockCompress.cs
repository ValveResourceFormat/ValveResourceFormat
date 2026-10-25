using System.Buffers;
using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Compression
{
    /// <summary>
    /// Decompresses Valve's block compressed buffers.
    /// </summary>
    public static class BlockCompress
    {
        /// <summary>
        /// Contains information about compression state and size.
        /// </summary>
        public record struct CompressionInfo(bool IsCompressed, int Size);

        /// <summary>
        /// Gets the decompressed size from a binary reader.
        /// </summary>
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

        /// <summary>
        /// Performs fast decompression of block-compressed data into the result buffer.
        /// </summary>
        public static void FastDecompress(CompressionInfo info, BinaryReader reader, Span<byte> result)
        {
            ArgumentOutOfRangeException.ThrowIfLessThan(result.Length, info.Size);

            if (!info.IsCompressed)
            {
                reader.ReadExactly(result[0..info.Size]);
                return;
            }

            // Every 16 tokens start with a 2 byte mask, and no token is longer than its output
            var stream = reader.BaseStream;
            var maxInputSize = (int)Math.Min(info.Size + (info.Size + 15L) / 16 * 2, stream.Length - stream.Position);
            var input = ArrayPool<byte>.Shared.Rent(maxInputSize);

            try
            {
                stream.ReadExactly(input, 0, maxInputSize);
                var consumed = FastDecompress(input.AsSpan(0, maxInputSize), result, info.Size);
                stream.Position -= maxInputSize - consumed;
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(input);
            }
        }

        private static int FastDecompress(ReadOnlySpan<byte> input, Span<byte> result, int size)
        {
            var inputPosition = 0;
            var position = 0;
            var blockMask = 0;
            var i = 0;

            while (position < size)
            {
                if (i == 0)
                {
                    blockMask = ReadUInt16(input, ref inputPosition);
                    i = 16;
                }

                if ((blockMask & 1) > 0)
                {
                    var offsetSize = ReadUInt16(input, ref inputPosition);
                    var offset = (offsetSize >> 4) + 1;
                    var length = (offsetSize & 0xF) + 3;

                    if (offset > position || length > size - position)
                    {
                        throw new InvalidDataException("Block compressed back reference is outside of the output.");
                    }

                    var source = result.Slice(position - offset, length);
                    var destination = result.Slice(position, length);
                    position += length;

                    if (offset >= length)
                    {
                        source.CopyTo(destination);
                    }
                    else if (offset == 1)
                    {
                        destination.Fill(source[0]);
                    }
                    else
                    {
                        for (var j = 0; j < length; j++)
                        {
                            destination[j] = source[j];
                        }
                    }
                }
                else
                {
                    if (inputPosition >= input.Length)
                    {
                        throw new InvalidDataException("Block compressed data is truncated.");
                    }

                    result[position++] = input[inputPosition++];
                }

                blockMask >>= 1;
                i--;
            }

            return inputPosition;
        }

        private static ushort ReadUInt16(ReadOnlySpan<byte> input, ref int inputPosition)
        {
            if (input.Length - inputPosition < sizeof(ushort))
            {
                throw new InvalidDataException("Block compressed data is truncated.");
            }

            var value = BinaryPrimitives.ReadUInt16LittleEndian(input[inputPosition..]);
            inputPosition += sizeof(ushort);
            return value;
        }
    }
}

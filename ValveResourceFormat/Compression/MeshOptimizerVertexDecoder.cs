using System.Buffers;
using System.Diagnostics;
using System.Runtime.CompilerServices;

namespace ValveResourceFormat.Compression
{
    /// <summary>
    /// Provides decoding functionality for mesh optimizer vertex buffers.
    /// </summary>
    /// <seealso href="https://github.com/zeux/meshoptimizer/blob/master/src/vertexcodec.cpp">This is a C# port of meshoptimizer.</seealso>
    public static partial class MeshOptimizerVertexDecoder
    {
        private const byte VertexHeader = 0xa0;
        private const int DecodeVertexVersion = 1;

        private const int VertexBlockSizeBytes = 8192;
        private const int VertexBlockMaxSize = 256;
        private const int ByteGroupSize = 16;
        private const int ByteGroupDecodeLimit = 24;
        private const int TailMinSizeV0 = 32;
        private const int TailMinSizeV1 = 24;

        private static readonly int[] BitsV0 = [0, 2, 4, 8];
        private static readonly int[] BitsV1 = [0, 1, 2, 4, 8];

        private static int GetVertexBlockSize(int vertexSize)
        {
            var result = VertexBlockSizeBytes / vertexSize;
            result &= ~(ByteGroupSize - 1);

            return result < VertexBlockMaxSize ? result : VertexBlockMaxSize;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static uint Rotate32(uint v, int r)
        {
            return (v << r) | (v >> ((32 - r) & 31));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static byte Unzigzag8(byte v)
        {
            return (byte)((0 - (v & 1)) ^ (v >> 1));
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static ushort Unzigzag16(ushort v)
        {
            return (ushort)((0 - (v & 1)) ^ (v >> 1));
        }

        private static ReadOnlySpan<byte> DecodeBytesGroup(ReadOnlySpan<byte> data, Span<byte> destination, int bits)
        {
            int dataVar;
            byte b;
            byte enc;

            [MethodImpl(MethodImplOptions.AggressiveInlining)]
            byte Next(byte bits, byte encv)
            {
                enc = b;
                enc >>= 8 - bits;
                b <<= bits;

                if (enc == (1 << bits) - 1)
                {
                    dataVar += 1;
                    return encv;
                }

                return enc;
            }

            switch (bits)
            {
                case 0:
                    for (var k = 0; k < ByteGroupSize; k++)
                    {
                        destination[k] = 0;
                    }

                    return data;
                case 1:
                    dataVar = 2;

                    // 2 groups with 8 1-bit values in each byte (reversed from the order in other groups)
                    b = data[0];
                    b = (byte)(((b * 0x80200802UL) & 0x0884422110UL) * 0x0101010101UL >> 32);

                    destination[0] = Next(1, data[dataVar]);
                    destination[1] = Next(1, data[dataVar]);
                    destination[2] = Next(1, data[dataVar]);
                    destination[3] = Next(1, data[dataVar]);
                    destination[4] = Next(1, data[dataVar]);
                    destination[5] = Next(1, data[dataVar]);
                    destination[6] = Next(1, data[dataVar]);
                    destination[7] = Next(1, data[dataVar]);

                    b = data[1];
                    b = (byte)(((b * 0x80200802UL) & 0x0884422110UL) * 0x0101010101UL >> 32);

                    destination[8] = Next(1, data[dataVar]);
                    destination[9] = Next(1, data[dataVar]);
                    destination[10] = Next(1, data[dataVar]);
                    destination[11] = Next(1, data[dataVar]);
                    destination[12] = Next(1, data[dataVar]);
                    destination[13] = Next(1, data[dataVar]);
                    destination[14] = Next(1, data[dataVar]);
                    destination[15] = Next(1, data[dataVar]);

                    return data[dataVar..];
                case 2:
                    dataVar = 4;

                    b = data[0];
                    destination[0] = Next(2, data[dataVar]);
                    destination[1] = Next(2, data[dataVar]);
                    destination[2] = Next(2, data[dataVar]);
                    destination[3] = Next(2, data[dataVar]);

                    b = data[1];
                    destination[4] = Next(2, data[dataVar]);
                    destination[5] = Next(2, data[dataVar]);
                    destination[6] = Next(2, data[dataVar]);
                    destination[7] = Next(2, data[dataVar]);

                    b = data[2];
                    destination[8] = Next(2, data[dataVar]);
                    destination[9] = Next(2, data[dataVar]);
                    destination[10] = Next(2, data[dataVar]);
                    destination[11] = Next(2, data[dataVar]);

                    b = data[3];
                    destination[12] = Next(2, data[dataVar]);
                    destination[13] = Next(2, data[dataVar]);
                    destination[14] = Next(2, data[dataVar]);
                    destination[15] = Next(2, data[dataVar]);

                    return data[dataVar..];
                case 4:
                    dataVar = 8;

                    b = data[0];
                    destination[0] = Next(4, data[dataVar]);
                    destination[1] = Next(4, data[dataVar]);

                    b = data[1];
                    destination[2] = Next(4, data[dataVar]);
                    destination[3] = Next(4, data[dataVar]);

                    b = data[2];
                    destination[4] = Next(4, data[dataVar]);
                    destination[5] = Next(4, data[dataVar]);

                    b = data[3];
                    destination[6] = Next(4, data[dataVar]);
                    destination[7] = Next(4, data[dataVar]);

                    b = data[4];
                    destination[8] = Next(4, data[dataVar]);
                    destination[9] = Next(4, data[dataVar]);

                    b = data[5];
                    destination[10] = Next(4, data[dataVar]);
                    destination[11] = Next(4, data[dataVar]);

                    b = data[6];
                    destination[12] = Next(4, data[dataVar]);
                    destination[13] = Next(4, data[dataVar]);

                    b = data[7];
                    destination[14] = Next(4, data[dataVar]);
                    destination[15] = Next(4, data[dataVar]);

                    return data[dataVar..];
                case 8:
                    data[..ByteGroupSize].CopyTo(destination);

                    return data[ByteGroupSize..];
                default:
                    throw new ArgumentException("Unexpected bit length");
            }
        }

        private static ReadOnlySpan<byte> DecodeBytes(ReadOnlySpan<byte> data, Span<byte> destination, ReadOnlySpan<int> bits)
        {
            if (destination.Length % ByteGroupSize != 0)
            {
                throw new ArgumentException("Expected data length to be a multiple of ByteGroupSize.");
            }

            var headerSize = ((destination.Length / ByteGroupSize) + 3) / 4;
            var header = data[..headerSize];

            data = data[headerSize..];

            for (var i = 0; i < destination.Length; i += ByteGroupSize)
            {
                if (data.Length < ByteGroupDecodeLimit)
                {
                    throw new InvalidOperationException("Cannot decode");
                }

                var headerOffset = i / ByteGroupSize;

                var bitsk = (header[headerOffset / 4] >> ((headerOffset % 4) * 2)) & 3;

                data = DecodeBytesGroup(data, destination[i..], bits[bitsk]);
            }

            return data;
        }

        private static ReadOnlySpan<byte> DecodeDeltas1(int size, ReadOnlySpan<byte> buffer, Span<byte> transposed, int vertexCount, int vertexSize, ReadOnlySpan<byte> lastVertex, int rot)
        {
            for (var k = 0; k < 4; k += size)
            {
                var vertexOffset = k;

                // Original code is based on a template, so instead of uint here it uses <T>,
                // but doing generics in C# like this is a pain, so we always just use uint.
                uint p = lastVertex[0];
                for (var j = 1; j < size; ++j)
                {
                    p |= (uint)(lastVertex[j] << (8 * j));
                }

                for (var i = 0; i < vertexCount; i++)
                {
                    uint v = buffer[i];
                    for (var j = 1; j < size; ++j)
                    {
                        v |= (uint)(buffer[i + vertexCount * j] << (8 * j));
                    }

                    v = size switch
                    {
                        1 => Unzigzag8((byte)v) + p,
                        2 => Unzigzag16((ushort)v) + p,
                        4 => Rotate32(v, rot) ^ p,
                        _ => throw new UnreachableException(),
                    };

                    for (var j = 0; j < size; ++j)
                    {
                        transposed[vertexOffset + j] = (byte)(v >> (j * 8));
                    }

                    p = v;
                    vertexOffset += vertexSize;
                }

                buffer = buffer[(vertexCount * size)..];
                lastVertex = lastVertex[size..];
            }

            return buffer;
        }

        private static ReadOnlySpan<byte> DecodeVertexBlock(ReadOnlySpan<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize, Span<byte> lastVertex, ReadOnlySpan<byte> channels, int version)
        {
            if (vertexCount <= 0 || vertexCount > VertexBlockMaxSize)
            {
                throw new ArgumentException("Expected vertexCount to be between 0 and VertexMaxBlockSize");
            }

            var bufferPool = ArrayPool<byte>.Shared.Rent(VertexBlockMaxSize * 4);
            var buffer = bufferPool.AsSpan(0, VertexBlockMaxSize * 4);

            var transposedPool = ArrayPool<byte>.Shared.Rent(VertexBlockSizeBytes);
            var transposed = transposedPool.AsSpan(0, VertexBlockSizeBytes);

            var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);
            var controlSize = version == 0 ? 0 : vertexSize / 4;

            try
            {
                var control = data[..controlSize];
                data = data[controlSize..];

                for (var k = 0; k < vertexSize; k += 4)
                {
                    var ctrlByte = version == 0 ? (byte)0 : control[k / 4];

                    for (var j = 0; j < 4; ++j)
                    {
                        var ctrl = (ctrlByte >> (j * 2)) & 3;

                        if (ctrl == 3)
                        {
                            // Literal encoding
                            if (data.Length < vertexCount)
                            {
                                throw new InvalidOperationException("Data buffer too small for literal encoding.");
                            }

                            data[..vertexCount].CopyTo(buffer.Slice(j * vertexCount, vertexCount));
                            data = data[vertexCount..];
                        }
                        else if (ctrl == 2)
                        {
                            // Zero encoding
                            buffer.Slice(j * vertexCount, vertexCount).Clear();
                        }
                        else
                        {
                            data = DecodeBytes(data, buffer.Slice(j * vertexCount, vertexCountAligned), version == 0 ? BitsV0 : BitsV1.AsSpan(ctrl));
                        }
                    }

                    var channel = version == 0 ? 0 : channels[k / 4];

                    switch (channel & 3)
                    {
                        case 0:
                            DecodeDeltas1(1, buffer, transposed[k..], vertexCount, vertexSize, lastVertex[k..], 0);
                            break;
                        case 1:
                            DecodeDeltas1(2, buffer, transposed[k..], vertexCount, vertexSize, lastVertex[k..], 0);
                            break;
                        case 2:
                            DecodeDeltas1(4, buffer, transposed[k..], vertexCount, vertexSize, lastVertex[k..], (32 - (channel >> 4)) & 31);
                            break;
                        default:
                            throw new InvalidOperationException("Invalid channel type");
                    }
                }

                transposed[..(vertexCount * vertexSize)].CopyTo(vertexData);

                transposed.Slice(vertexSize * (vertexCount - 1), vertexSize).CopyTo(lastVertex);
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(bufferPool);
                ArrayPool<byte>.Shared.Return(transposedPool);
            }

            return data;
        }

        /// <summary>
        /// Decodes a vertex buffer from compressed format.
        /// </summary>
        public static byte[] DecodeVertexBuffer(int vertexCount, int vertexSize, ReadOnlySpan<byte> buffer, bool useSimd = true)
        {
            if (vertexSize <= 0 || vertexSize > 256)
            {
                throw new ArgumentException("Vertex size is expected to be between 1 and 256");
            }

            if (vertexSize % 4 != 0)
            {
                throw new ArgumentException("Vertex size is expected to be a multiple of 4.");
            }

            if (buffer.Length < 1)
            {
                throw new ArgumentException("Vertex buffer is too short.");
            }

            if ((buffer[0] & 0xF0) != VertexHeader)
            {
                throw new ArgumentException($"Invalid vertex buffer header, expected {VertexHeader} but got {buffer[0]}.");
            }

            var version = buffer[0] & 0x0F;

            if (version > DecodeVertexVersion)
            {
                throw new ArgumentException($"Incorrect vertex buffer encoding version, got {version}.");
            }

            buffer = buffer[1..];

            // Determine tail size based on version
            var tailSize = vertexSize + (version == 0 ? 0 : vertexSize / 4);
            var tailSizeMin = version == 0 ? TailMinSizeV0 : TailMinSizeV1;
            var tailSizePadded = tailSize < tailSizeMin ? tailSizeMin : tailSize;

            if (buffer.Length < tailSizePadded)
            {
                throw new ArgumentException("Buffer too small to contain tail data.");
            }

            var resultArray = new byte[vertexCount * vertexSize];

            // C code always uses [256] here, but more than vertexSize can't be used
            var lastVertexBuffer = ArrayPool<byte>.Shared.Rent(vertexSize);
            var lastVertex = lastVertexBuffer.AsSpan(0, vertexSize);

            try
            {
                buffer.Slice(buffer.Length - tailSize, vertexSize).CopyTo(lastVertex);

                var channels = version == 0 ? null : buffer.Slice(buffer.Length - tailSize + vertexSize, vertexSize / 4);

                var vertexBlockSize = GetVertexBlockSize(vertexSize);

                var vertexOffset = 0;

                var result = resultArray.AsSpan();

                useSimd &= IsHardwareAccelerated;

                while (vertexOffset < vertexCount)
                {
                    var blockSize = vertexOffset + vertexBlockSize < vertexCount
                        ? vertexBlockSize
                        : vertexCount - vertexOffset;

                    var vertexData = result[(vertexOffset * vertexSize)..];

                    if (useSimd)
                    {
                        buffer = DecodeVertexBlockSimd(buffer, vertexData, blockSize, vertexSize, lastVertex, channels, version);
                    }
                    else
                    {
                        buffer = DecodeVertexBlock(buffer, vertexData, blockSize, vertexSize, lastVertex, channels, version);
                    }

                    vertexOffset += blockSize;
                }
            }
            finally
            {
                ArrayPool<byte>.Shared.Return(lastVertexBuffer);
            }

            if (buffer.Length != tailSizePadded)
            {
                throw new ArgumentException("Tail size incorrect");
            }

            return resultArray;
        }
    }
}

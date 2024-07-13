/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/vertexcodec.cpp
 */
using System.Buffers;

namespace ValveResourceFormat.Compression
{
    public static class MeshOptimizerVertexDecoder
    {
        private const byte VertexHeader = 0xa0;

        private const int VertexBlockSizeBytes = 8192;
        private const int VertexBlockMaxSize = 256;
        private const int ByteGroupSize = 16;
        private const int TailMaxSize = 32;

        private static int GetVertexBlockSize(int vertexSize)
        {
            var result = VertexBlockSizeBytes / vertexSize;
            result &= ~(ByteGroupSize - 1);

            return result < VertexBlockMaxSize ? result : VertexBlockMaxSize;
        }

        private static byte Unzigzag8(byte v)
        {
            return (byte)(-(v & 1) ^ (v >> 1));
        }

        private static Span<byte> DecodeBytesGroup(Span<byte> data, Span<byte> destination, int bitslog2)
        {
            int dataVar;
            byte b;
            byte enc;

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

            switch (bitslog2)
            {
                case 0:
                    for (var k = 0; k < ByteGroupSize; k++)
                    {
                        destination[k] = 0;
                    }

                    return data;
                case 1:
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
                case 2:
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
                case 3:
                    data[..ByteGroupSize].CopyTo(destination);

                    return data[ByteGroupSize..];
                default:
                    throw new ArgumentException("Unexpected bit length");
            }
        }

        private static Span<byte> DecodeBytes(Span<byte> data, Span<byte> destination)
        {
            if (destination.Length % ByteGroupSize != 0)
            {
                throw new ArgumentException("Expected data length to be a multiple of ByteGroupSize.");
            }

            var headerSize = ((destination.Length / ByteGroupSize) + 3) / 4;
            var header = data[..];

            data = data[headerSize..];

            for (var i = 0; i < destination.Length; i += ByteGroupSize)
            {
                if (data.Length < TailMaxSize)
                {
                    throw new InvalidOperationException("Cannot decode");
                }

                var headerOffset = i / ByteGroupSize;

                var bitslog2 = (header[headerOffset / 4] >> (headerOffset % 4 * 2)) & 3;

                data = DecodeBytesGroup(data, destination[i..], bitslog2);
            }

            return data;
        }

        private static Span<byte> DecodeVertexBlock(Span<byte> data, Span<byte> vertexData, int vertexCount, int vertexSize, Span<byte> lastVertex)
        {
            if (vertexCount <= 0 || vertexCount > VertexBlockMaxSize)
            {
                throw new ArgumentException("Expected vertexCount to be between 0 and VertexMaxBlockSize");
            }

            var bufferPool = ArrayPool<byte>.Shared.Rent(VertexBlockMaxSize);
            var buffer = bufferPool.AsSpan(0, VertexBlockMaxSize);
            var transposedPool = ArrayPool<byte>.Shared.Rent(VertexBlockSizeBytes);
            var transposed = transposedPool.AsSpan(0, VertexBlockSizeBytes);

            try
            {
                var vertexCountAligned = (vertexCount + ByteGroupSize - 1) & ~(ByteGroupSize - 1);

                for (var k = 0; k < vertexSize; ++k)
                {
                    data = DecodeBytes(data, buffer[..vertexCountAligned]);

                    var vertexOffset = k;

                    var p = lastVertex[k];

                    for (var i = 0; i < vertexCount; ++i)
                    {
                        var v = (byte)(Unzigzag8(buffer[i]) + p);

                        transposed[vertexOffset] = v;
                        p = v;

                        vertexOffset += vertexSize;
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

        public static byte[] DecodeVertexBuffer(int vertexCount, int vertexSize, Span<byte> buffer)
        {
            if (vertexSize <= 0 || vertexSize > 256)
            {
                throw new ArgumentException("Vertex size is expected to be between 1 and 256");
            }

            if (vertexSize % 4 != 0)
            {
                throw new ArgumentException("Vertex size is expected to be a multiple of 4.");
            }

            if (buffer.Length < 1 + vertexSize)
            {
                throw new ArgumentException("Vertex buffer is too short.");
            }

            if ((buffer[0] & 0xF0) != VertexHeader)
            {
                throw new ArgumentException($"Invalid vertex buffer header, expected {VertexHeader} but got {buffer[0]}.");
            }

            var version = buffer[0] & 0x0F;

            if (version > 0)
            {
                throw new ArgumentException("Incorrect vertex buffer encoding version.");
            }

            buffer = buffer[1..];

            var lastVertex = new byte[vertexSize];
            buffer.Slice(buffer.Length - vertexSize, vertexSize).CopyTo(lastVertex);

            var vertexBlockSize = GetVertexBlockSize(vertexSize);

            var vertexOffset = 0;

            var resultArray = new byte[vertexCount * vertexSize];
            var result = resultArray.AsSpan();

            while (vertexOffset < vertexCount)
            {
                var blockSize = vertexOffset + vertexBlockSize < vertexCount
                    ? vertexBlockSize
                    : vertexCount - vertexOffset;

                buffer = DecodeVertexBlock(buffer, result[(vertexOffset * vertexSize)..], blockSize, vertexSize, lastVertex);

                vertexOffset += blockSize;
            }

            return resultArray;
        }
    }
}

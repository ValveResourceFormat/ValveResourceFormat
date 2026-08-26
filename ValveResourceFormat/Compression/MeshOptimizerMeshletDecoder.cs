using System;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Compression
{
    /// <summary>
    /// Provides decoding functionality for mesh optimizer meshlet data.
    /// </summary>
    /// <seealso href="https://github.com/zeux/meshoptimizer/blob/master/src/meshletcodec.cpp">This is a C# port of meshoptimizer.</seealso>
    public static class MeshOptimizerMeshletDecoder
    {
        /// <summary>
        /// Decodes meshlet vertex and triangle data from a compressed buffer.
        /// Vertices are written as uint (vertexSize=4) or ushort (vertexSize=2).
        /// Triangles are written as packed uint (triangleSize=4) or 3 separate bytes (triangleSize=3).
        /// </summary>
        public static void DecodeMeshlet(Span<byte> vertices, int vertexCount, int vertexSize,
            Span<byte> triangles, int triangleCount, int triangleSize,
            ReadOnlySpan<byte> buffer)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(vertexCount, 256);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(triangleCount, 256);

            if (vertexSize != 2 && vertexSize != 4)
            {
                throw new ArgumentException("vertexSize must be 2 or 4", nameof(vertexSize));
            }

            if (triangleSize != 3 && triangleSize != 4)
            {
                throw new ArgumentException("triangleSize must be 3 or 4", nameof(triangleSize));
            }

            var codesSize = (triangleCount + 1) / 2;
            var ctrlSize = (vertexCount + 3) / 4;
            var gapSize = (codesSize + ctrlSize < 16) ? 16 - (codesSize + ctrlSize) : 0;

            if (buffer.Length < codesSize + ctrlSize + gapSize)
            {
                throw new InvalidOperationException("Buffer too small for meshlet data.");
            }

            var end = buffer.Length;
            var codes = buffer[(end - codesSize)..];
            var ctrl = buffer[(end - codesSize - ctrlSize)..];
            var data = buffer;
            var boundOffset = end - codesSize - ctrlSize - gapSize;

            Span<uint> decodedVertices = stackalloc uint[vertexCount];
            var dataOffset = DecodeVertices(decodedVertices, ctrl, data, boundOffset, vertexCount);

            if (dataOffset < 0)
            {
                throw new InvalidOperationException("Failed to decode meshlet vertices.");
            }

            for (var i = 0; i < vertexCount; i++)
            {
                if (vertexSize == 4)
                {
                    Unsafe.As<byte, uint>(ref vertices[i * 4]) = decodedVertices[i];
                }
                else
                {
                    Unsafe.As<byte, ushort>(ref vertices[i * 2]) = (ushort)decodedVertices[i];
                }
            }

            int endOffset;
            if (triangleSize == 4)
            {
                Span<byte> decodedTriangles = stackalloc byte[triangleCount * 3];
                endOffset = DecodeTriangles(decodedTriangles, codes, data, dataOffset, boundOffset, triangleCount);

                var dst = MemoryMarshal.Cast<byte, uint>(triangles);
                for (var i = 0; i < triangleCount; i++)
                {
                    dst[i] = (uint)(decodedTriangles[i * 3] | (decodedTriangles[i * 3 + 1] << 8) | (decodedTriangles[i * 3 + 2] << 16));
                }
            }
            else
            {
                endOffset = DecodeTriangles(triangles, codes, data, dataOffset, boundOffset, triangleCount);
            }

            if (endOffset < 0)
            {
                throw new InvalidOperationException("Failed to decode meshlet triangles.");
            }

            if (endOffset != boundOffset)
            {
                throw new InvalidOperationException("Meshlet data did not decode to expected size.");
            }
        }

        /// <summary>
        /// Decodes meshlet vertex and triangle data in raw format (both as uint arrays).
        /// </summary>
        public static void DecodeMeshletRaw(Span<uint> vertices, int vertexCount,
            Span<uint> triangles, int triangleCount,
            ReadOnlySpan<byte> buffer)
        {
            ArgumentOutOfRangeException.ThrowIfGreaterThan(vertexCount, 256);
            ArgumentOutOfRangeException.ThrowIfGreaterThan(triangleCount, 256);

            var codesSize = (triangleCount + 1) / 2;
            var ctrlSize = (vertexCount + 3) / 4;
            var gapSize = (codesSize + ctrlSize < 16) ? 16 - (codesSize + ctrlSize) : 0;

            if (buffer.Length < codesSize + ctrlSize + gapSize)
            {
                throw new InvalidOperationException("Buffer too small for meshlet data.");
            }

            var end = buffer.Length;
            var codes = buffer[(end - codesSize)..];
            var ctrl = buffer[(end - codesSize - ctrlSize)..];
            var data = buffer;
            var boundOffset = end - codesSize - ctrlSize - gapSize;

            var dataOffset = DecodeVertices(vertices, ctrl, data, boundOffset, vertexCount);

            if (dataOffset < 0)
            {
                throw new InvalidOperationException("Failed to decode meshlet vertices.");
            }

            Span<byte> decodedTriangles = stackalloc byte[triangleCount * 3];
            var endOffset = DecodeTriangles(decodedTriangles, codes, data, dataOffset, boundOffset, triangleCount);

            for (var i = 0; i < triangleCount; i++)
            {
                triangles[i] = (uint)(decodedTriangles[i * 3] | (decodedTriangles[i * 3 + 1] << 8) | (decodedTriangles[i * 3 + 2] << 16));
            }

            if (endOffset < 0)
            {
                throw new InvalidOperationException("Failed to decode meshlet triangles.");
            }

            if (endOffset != boundOffset)
            {
                throw new InvalidOperationException("Meshlet data did not decode to expected size.");
            }
        }

        private static int DecodeVertices(Span<uint> vertices, ReadOnlySpan<byte> ctrl, ReadOnlySpan<byte> data, int boundOffset, int vertexCount)
        {
            var last = ~0u;
            var dataOffset = 0;

            for (var i = 0; i < vertexCount; i += 4)
            {
                if (dataOffset > boundOffset)
                {
                    return -1;
                }

                var code4 = ctrl[i / 4];

                for (var k = 0; k < 4; k++)
                {
                    var code = ((code4 >> k) & 1) | ((code4 >> (k + 3)) & 2);
                    var length = code4 == 0xFF ? 4 : code;

                    // Read up to 4 bytes little-endian; we need at least `length` bytes available
                    // but we read up to 4 branchlessly (safe because gap guarantees 16 bytes of overread)
                    uint v = 0;
                    if (length > 0)
                    {
                        v = data[dataOffset];
                    }

                    if (length > 1)
                    {
                        v |= (uint)data[dataOffset + 1] << 8;
                    }

                    if (length > 2)
                    {
                        v |= (uint)data[dataOffset + 2] << 16;
                    }

                    if (length > 3)
                    {
                        v |= (uint)data[dataOffset + 3] << 24;
                    }

                    // unzigzag + 1
                    var d = (v >> 1) ^ (uint)(-(int)(v & 1));
                    var r = last + d + 1;

                    if (i + k < vertexCount)
                    {
                        vertices[i + k] = r;
                    }

                    dataOffset += length;
                    last = r;
                }
            }

            return dataOffset;
        }

        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        private static void WriteTriangle(Span<byte> triangles, int i, uint fifo)
        {
            triangles[i * 3 + 0] = (byte)(fifo >> 8);
            triangles[i * 3 + 1] = (byte)(fifo >> 16);
            triangles[i * 3 + 2] = (byte)(fifo >> 24);
        }

        private static int DecodeTriangles(Span<byte> triangles, ReadOnlySpan<byte> codes, ReadOnlySpan<byte> data, int dataOffset, int boundOffset, int triangleCount)
        {
            uint next = 0;
            Span<uint> fifo = stackalloc uint[3];

            for (var i = 0; i < triangleCount; i++)
            {
                if (dataOffset > boundOffset)
                {
                    return -1;
                }

                var code = (uint)((codes[i / 2] >> ((i & 1) * 4)) & 0xF);
                uint tri;

                if (code < 12)
                {
                    var edge = fifo[(int)(code / 4)];
                    edge >>= (int)((code << 3) & 16);

                    var e = data[dataOffset];
                    var c = (code & 1) != 0 ? (uint)e : next;
                    dataOffset += (int)(code & 1);
                    next += 1 - (code & 1);

                    tri = ((edge & 0xff) << 16) | (edge & 0xff00) | c | (c << 24);
                }
                else
                {
                    var fea = code > 12 ? 1 : 0;
                    var feb = code > 13 ? 1 : 0;
                    var fec = code > 14 ? 1 : 0;

                    uint e;

                    e = data[dataOffset];
                    var a = fea != 0 ? e : next;
                    dataOffset += fea;
                    next += (uint)(1 - fea);

                    e = data[dataOffset];
                    var b = feb != 0 ? e : next;
                    dataOffset += feb;
                    next += (uint)(1 - feb);

                    e = data[dataOffset];
                    var c = fec != 0 ? e : next;
                    dataOffset += fec;
                    next += (uint)(1 - fec);

                    tri = c | (a << 8) | (b << 16) | (c << 24);
                }

                WriteTriangle(triangles, i, tri);

                fifo[2] = fifo[1];
                fifo[1] = fifo[0];
                fifo[0] = tri;
            }

            return dataOffset;
        }
    }
}

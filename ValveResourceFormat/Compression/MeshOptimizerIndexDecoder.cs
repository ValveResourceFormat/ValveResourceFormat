using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Compression
{
    /// <summary>
    /// Provides decoding functionality for mesh optimizer index buffers.
    /// </summary>
    /// <seealso href="https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp">This is a C# port of meshoptimizer.</seealso>
    public static class MeshOptimizerIndexDecoder
    {
        private const byte IndexHeader = 0xe0;
        private const int DecodeIndexVersion = 1;

        private static void PushEdgeFifo(Span<(uint, uint)> fifo, ref int offset, uint a, uint b)
        {
            fifo[offset] = (a, b);
            offset = (offset + 1) & 15;
        }

        private static void PushVertexFifo(Span<uint> fifo, ref int offset, uint v, bool cond = true)
        {
            fifo[offset] = v;
            offset = (offset + (cond ? 1 : 0)) & 15;
        }

        private static uint DecodeVByte(ReadOnlySpan<byte> data, ref int position)
        {
            var lead = (uint)data[position++];

            // fast path: single byte
            if (lead < 128)
            {
                return lead;
            }

            // slow path: up to 4 extra bytes
            // note that this loop always terminates, which is important for malformed data
            var result = lead & 127;
            var shift = 7;

            for (var i = 0; i < 4; i++)
            {
                var group = (uint)data[position++];
                result |= (group & 127) << shift;
                shift += 7;

                if (group < 128)
                {
                    break;
                }
            }

            return result;
        }

        private static uint DecodeIndex(ReadOnlySpan<byte> data, uint last, ref int position)
        {
            var v = DecodeVByte(data, ref position);
            var d = (uint)((v >> 1) ^ -(v & 1));

            return last + d;
        }

        private static Span<byte> WriteTriangle(Span<byte> destination, int indexSize, uint a, uint b, uint c)
        {
            if (indexSize == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination, (ushort)a);
                BinaryPrimitives.WriteUInt16LittleEndian(destination[2..], (ushort)b);
                BinaryPrimitives.WriteUInt16LittleEndian(destination[4..], (ushort)c);

                return destination[6..];
            }

            BinaryPrimitives.WriteUInt32LittleEndian(destination, a);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[4..], b);
            BinaryPrimitives.WriteUInt32LittleEndian(destination[8..], c);

            return destination[12..];
        }

        /// <summary>
        /// Decodes an index buffer from compressed format.
        /// </summary>
        public static byte[] DecodeIndexBuffer(int indexCount, int indexSize, ReadOnlySpan<byte> buffer)
        {
            if (indexCount % 3 != 0)
            {
                throw new ArgumentException("Expected indexCount to be a multiple of 3.");
            }

            if (indexSize != 2 && indexSize != 4)
            {
                throw new ArgumentException("Expected indexSize to be either 2 or 4");
            }

            var dataOffset = 1 + (indexCount / 3);

            // the minimum valid encoding is header, 1 byte per triangle and a 16-byte codeaux table
            if (buffer.Length < dataOffset + 16)
            {
                throw new ArgumentException("Index buffer is too short.");
            }

            if ((buffer[0] & 0xF0) != IndexHeader)
            {
                throw new ArgumentException($"Invalid index buffer header, expected {IndexHeader} but got {buffer[0]}.");
            }

            var version = buffer[0] & 0x0F;

            if (version > DecodeIndexVersion)
            {
                throw new ArgumentException($"Incorrect index buffer encoding version, got {version}.");
            }

            Span<(uint, uint)> edgeFifo = stackalloc (uint, uint)[16];
            Span<uint> vertexFifo = stackalloc uint[16];

            var edgeFifoOffset = 0;
            var vertexFifoOffset = 0;

            var next = 0u;
            var last = 0u;

            var fecmax = version >= 1 ? 13 : 15;

            // since we store 16-byte codeaux table at the end, triangle data has to begin before data_safe_end
            var code = buffer[1..dataOffset];
            var data = buffer[dataOffset..];
            var position = 0;

            // each triangle reads at most 16 bytes of data: 1b for codeaux and 5b for each free index
            var dataSafeEnd = data.Length - 16;

            var codeauxTable = data[dataSafeEnd..];

            var destinationArray = new byte[indexCount * indexSize];
            var destination = destinationArray.AsSpan();

            foreach (var codetri in code)
            {
                if (codetri < 0xf0)
                {
                    var fe = codetri >> 4;

                    // fifo reads are wrapped around 16 entry buffer
                    var (a, b) = edgeFifo[(edgeFifoOffset - 1 - fe) & 15];
                    var c = 0u;

                    var fec = codetri & 15;

                    // note: this is the most common path in the entire decoder
                    // inside this if we try to stay branchless (by using cmov/etc.) since these aren't predictable
                    if (fec < fecmax)
                    {
                        // fifo reads are wrapped around 16 entry buffer
                        var cf = vertexFifo[(vertexFifoOffset - 1 - fec) & 15];

                        c = (fec == 0) ? next : cf;

                        var fec0 = fec == 0;
                        next += fec0 ? 1u : 0u;

                        // push vertex fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, fec0);
                    }
                    else
                    {
                        // make sure we have enough data to read for a triangle; this check covers worst case advance
                        if (position > dataSafeEnd)
                        {
                            throw new InvalidDataException("Index buffer data is truncated.");
                        }

                        // fec * 2 - 27 decodes 13, 14 into -1, 1
                        // note that we need to update the last index since free indices are delta-encoded
                        last = c = (fec != 15) ? last + (uint)(fec * 2 - 27) : DecodeIndex(data, last, ref position);

                        // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                        PushVertexFifo(vertexFifo, ref vertexFifoOffset, c);
                    }

                    // push edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);

                    // output triangle
                    destination = WriteTriangle(destination, indexSize, a, b, c);
                }
                else if (codetri < 0xfe)
                {
                    // fast path: read codeaux from the table
                    var codeaux = codeauxTable[codetri & 15];

                    // note: table can't contain feb/fec=15
                    var feb = codeaux >> 4;
                    var fec = codeaux & 15;

                    // fifo reads are wrapped around 16 entry buffer
                    // also note that we increment next for all three vertices before decoding indices - this matches encoder behavior
                    var a = next++;

                    var bf = vertexFifo[(vertexFifoOffset - feb) & 15];
                    var b = (feb == 0) ? next : bf;

                    var feb0 = feb == 0;
                    next += feb0 ? 1u : 0u;

                    var cf = vertexFifo[(vertexFifoOffset - fec) & 15];
                    var c = (fec == 0) ? next : cf;

                    var fec0 = fec == 0;
                    next += fec0 ? 1u : 0u;

                    // output triangle
                    destination = WriteTriangle(destination, indexSize, a, b, c);

                    // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, a);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, b, feb0);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, fec0);

                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, b, a);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                }
                else
                {
                    // make sure we have enough data to read for a triangle; this check covers worst case advance
                    if (position > dataSafeEnd)
                    {
                        throw new InvalidDataException("Index buffer data is truncated.");
                    }

                    // slow path: read a full byte for codeaux instead of using a table lookup
                    var codeaux = data[position++];

                    var fea = codetri == 0xfe ? 0 : 15;
                    var feb = codeaux >> 4;
                    var fec = codeaux & 15;

                    // reset: codeaux is 0 but encoded as not-a-table
                    if (codeaux == 0)
                    {
                        next = 0;
                    }

                    // fifo reads are wrapped around 16 entry buffer
                    // also note that we increment next for all three vertices before decoding indices - this matches encoder behavior
                    var a = (fea == 0) ? next++ : 0;
                    var b = (feb == 0) ? next++ : vertexFifo[(vertexFifoOffset - feb) & 15];
                    var c = (fec == 0) ? next++ : vertexFifo[(vertexFifoOffset - fec) & 15];

                    // note that we need to update the last index since free indices are delta-encoded
                    if (fea == 15)
                    {
                        last = a = DecodeIndex(data, last, ref position);
                    }

                    if (feb == 15)
                    {
                        last = b = DecodeIndex(data, last, ref position);
                    }

                    if (fec == 15)
                    {
                        last = c = DecodeIndex(data, last, ref position);
                    }

                    // output triangle
                    destination = WriteTriangle(destination, indexSize, a, b, c);

                    // push vertex/edge fifo must match the encoding step *exactly* otherwise the data will not be decoded correctly
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, a);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, b, (feb == 0) | (feb == 15));
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, (fec == 0) | (fec == 15));

                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, b, a);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                }
            }

            // we should've read all data bytes and stopped at the boundary between data and codeaux table
            if (position != dataSafeEnd)
            {
                throw new InvalidDataException("Index buffer data is malformed.");
            }

            return destinationArray;
        }
    }
}

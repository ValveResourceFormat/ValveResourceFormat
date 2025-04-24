/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp
 */
using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.Compression
{
    public static class MeshOptimizerIndexDecoder
    {
        private const byte IndexHeader = 0xe0;

        private static void PushEdgeFifo(Span<ValueTuple<uint, uint>> fifo, ref int offset, uint a, uint b)
        {
            fifo[offset] = (a, b);
            offset = (offset + 1) & 15;
        }

        private static void PushVertexFifo(Span<uint> fifo, ref int offset, uint v, bool cond = true)
        {
            fifo[offset] = v;
            offset = (offset + (cond ? 1 : 0)) & 15;
        }

        private static uint DecodeVByte(Span<byte> data, ref int position)
        {
            var lead = (uint)data[position++];

            if (lead < 128)
            {
                return lead;
            }

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

        private static uint DecodeIndex(Span<byte> data, uint last, ref int position)
        {
            var v = DecodeVByte(data, ref position);
            var d = (uint)((v >> 1) ^ -(v & 1));

            return last + d;
        }

        private static void WriteTriangle(Span<byte> destination, int offset, int indexSize, uint a, uint b, uint c)
        {
            offset *= indexSize;

            if (indexSize == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(offset + 0)..], (ushort)a);
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(offset + 2)..], (ushort)b);
                BinaryPrimitives.WriteUInt16LittleEndian(destination[(offset + 4)..], (ushort)c);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination[(offset + 0)..], a);
                BinaryPrimitives.WriteUInt32LittleEndian(destination[(offset + 4)..], b);
                BinaryPrimitives.WriteUInt32LittleEndian(destination[(offset + 8)..], c);
            }
        }

        public static byte[] DecodeIndexBuffer(int indexCount, int indexSize, Span<byte> buffer)
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

            if (version > 1)
            {
                throw new ArgumentException($"Incorrect index buffer encoding version, got {version}.");
            }

            Span<uint> vertexFifo = stackalloc uint[16];
            Span<ValueTuple<uint, uint>> edgeFifo = stackalloc ValueTuple<uint, uint>[16];
            var edgeFifoOffset = 0;
            var vertexFifoOffset = 0;

            var next = 0u;
            var last = 0u;

            var fecmax = version >= 1 ? 13 : 15;

            var bufferIndex = 1;
            var data = buffer[dataOffset..^16];

            var codeauxTable = buffer[^16..];

            var destinationArray = new byte[indexCount * indexSize];
            var destination = destinationArray.AsSpan();
            var position = 0;

            for (var i = 0; i < indexCount; i += 3)
            {
                var codetri = buffer[bufferIndex++];

                if (codetri < 0xf0)
                {
                    var fe = codetri >> 4;

                    var (a, b) = edgeFifo[(edgeFifoOffset - 1 - fe) & 15];

                    var fec = codetri & 15;

                    if (fec < fecmax)
                    {
                        var c = fec == 0 ? next : vertexFifo[(vertexFifoOffset - 1 - fec) & 15];

                        var fec0 = fec == 0;
                        next += fec0 ? 1u : 0u;

                        WriteTriangle(destination, i, indexSize, a, b, c);

                        PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, fec0);

                        PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                        PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                    }
                    else
                    {
                        var c = last = (fec != 15) ? last + (uint)(fec - (fec ^ 3)) : DecodeIndex(data, last, ref position);

                        WriteTriangle(destination, i, indexSize, a, b, c);

                        PushVertexFifo(vertexFifo, ref vertexFifoOffset, c);

                        PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                        PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                    }
                }
                else if (codetri < 0xfe)
                {
                    var codeaux = codeauxTable[codetri & 15];

                    var feb = codeaux >> 4;
                    var fec = codeaux & 15;

                    var a = next++;

                    var b = (feb == 0) ? next : vertexFifo[(vertexFifoOffset - feb) & 15];

                    var feb0 = feb == 0 ? 1u : 0u;
                    next += feb0;

                    var c = (fec == 0) ? next : vertexFifo[(vertexFifoOffset - fec) & 15];

                    var fec0 = fec == 0 ? 1u : 0u;
                    next += fec0;

                    WriteTriangle(destination, i, indexSize, a, b, c);

                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, a);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, b, feb0 == 1u);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, fec0 == 1u);

                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, b, a);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                }
                else
                {
                    var codeaux = (uint)data[position++];

                    var fea = codetri == 0xfe ? 0 : 15;
                    var feb = codeaux >> 4;
                    var fec = codeaux & 15;

                    if (codeaux == 0)
                    {
                        next = 0;
                    }

                    var a = (fea == 0) ? next++ : 0;
                    var b = (feb == 0) ? next++ : vertexFifo[(vertexFifoOffset - (int)feb) & 15];
                    var c = (fec == 0) ? next++ : vertexFifo[(vertexFifoOffset - (int)fec) & 15];

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

                    WriteTriangle(destination, i, indexSize, a, b, c);

                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, a);
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, b, (feb == 0) || (feb == 15));
                    PushVertexFifo(vertexFifo, ref vertexFifoOffset, c, (fec == 0) || (fec == 15));

                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, b, a);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                    PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                }
            }

            if (position != data.Length)
            {
                throw new InvalidDataException("we didn't read all data bytes and stopped before the boundary between data and codeaux table");
            }

            return destinationArray;
        }
    }
}

/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp
 */
using System;
using System.Buffers.Binary;
using System.IO;

namespace ValveResourceFormat.ThirdParty
{
    public class MeshOptimizerIndexDecoder
    {
        private const byte IndexHeader = 0xe0;

        private static void PushEdgeFifo(ValueTuple<uint, uint>[] fifo, ref int offset, uint a, uint b)
        {
            fifo[offset] = (a, b);
            offset = (offset + 1) & 15;
        }

        private static void PushVertexFifo(uint[] fifo, ref int offset, uint v, bool cond = true)
        {
            fifo[offset] = v;
            offset = (offset + (cond ? 1 : 0)) & 15;
        }

        private static uint DecodeVByte(BinaryReader data)
        {
            var lead = (uint)data.ReadByte();

            if (lead < 128)
            {
                return lead;
            }

            var result = lead & 127;
            var shift = 7;

            for (var i = 0; i < 4; i++)
            {
                var group = (uint)data.ReadByte();
                result |= (group & 127) << shift;
                shift += 7;

                if (group < 128)
                {
                    break;
                }
            }

            return result;
        }

        private static uint DecodeIndex(BinaryReader data, uint next, uint last)
        {
            var v = DecodeVByte(data);
            var d = (uint)((v >> 1) ^ -(v & 1));

            return last + d;
        }

        private static void WriteTriangle(Span<byte> destination, int offset, int indexSize, uint a, uint b, uint c)
        {
            offset *= indexSize;

            if (indexSize == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 0), (ushort)a);
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 2), (ushort)b);
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 4), (ushort)c);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 0), a);
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 4), b);
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 8), c);
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

            if (buffer[0] != IndexHeader)
            {
                throw new ArgumentException("Incorrect index buffer header.");
            }

            var vertexFifo = new uint[16];
            var edgeFifo = new ValueTuple<uint, uint>[16];
            var edgeFifoOffset = 0;
            var vertexFifoOffset = 0;

            var next = 0u;
            var last = 0u;

            var bufferIndex = 1;
            var data = buffer.Slice(dataOffset, buffer.Length - 16 - dataOffset);

            var codeauxTable = buffer.Slice(buffer.Length - 16);

            var destination = new Span<byte>(new byte[indexCount * indexSize]);

            using (var stream = new MemoryStream(data.ToArray()))
            using (var dataReader = new BinaryReader(stream))
            {
                for (var i = 0; i < indexCount; i += 3)
                {
                    var codetri = buffer[bufferIndex++];

                    if (codetri < 0xf0)
                    {
                        var fe = codetri >> 4;

                        var (a, b) = edgeFifo[(edgeFifoOffset - 1 - fe) & 15];

                        var fec = codetri & 15;

                        if (fec != 15)
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
                            var c = last = DecodeIndex(dataReader, next, last);

                            WriteTriangle(destination, i, indexSize, a, b, c);

                            PushVertexFifo(vertexFifo, ref vertexFifoOffset, c);

                            PushEdgeFifo(edgeFifo, ref edgeFifoOffset, c, b);
                            PushEdgeFifo(edgeFifo, ref edgeFifoOffset, a, c);
                        }
                    }
                    else
                    {
                        if (codetri < 0xfe)
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
                            var codeaux = (uint)dataReader.ReadByte();

                            var fea = codetri == 0xfe ? 0 : 15;
                            var feb = codeaux >> 4;
                            var fec = codeaux & 15;

                            var a = (fea == 0) ? next++ : 0;
                            var b = (feb == 0) ? next++ : vertexFifo[(vertexFifoOffset - feb) & 15];
                            var c = (fec == 0) ? next++ : vertexFifo[(vertexFifoOffset - fec) & 15];

                            if (fea == 15)
                            {
                                last = a = DecodeIndex(dataReader, next, last);
                            }

                            if (feb == 15)
                            {
                                last = b = DecodeIndex(dataReader, next, last);
                            }

                            if (fec == 15)
                            {
                                last = c = DecodeIndex(dataReader, next, last);
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
                }

                if (stream.Position != stream.Length)
                {
                    throw new InvalidDataException("we didn't read all data bytes and stopped before the boundary between data and codeaux table");
                }
            }

            return destination.ToArray();
        }
    }
}

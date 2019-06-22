/**
 * C# Port of https://github.com/zeux/meshoptimizer/blob/master/src/indexcodec.cpp
 */
using System;
using System.Buffers.Binary;
using System.Collections.Generic;
using System.IO;

namespace ValveResourceFormat.ThirdParty
{
    public class MeshOptimizerIndexDecoder
    {
        private const byte IndexHeader = 0xe0;

        private static void PushEdgeFifo(Queue<(uint, uint)> fifo, uint a, uint b)
        {
            fifo.Enqueue((a, b));
        }

        private static void PushVertexFifo(Queue<uint> fifo, uint v, bool cond = true)
        {
            if (!cond)
            {
                fifo.Dequeue();
            }

            fifo.Enqueue(v);
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
            if (indexSize == 2)
            {
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 0), (ushort)a);
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 2), (ushort)b);
                BinaryPrimitives.WriteUInt16LittleEndian(destination.Slice(offset + 4), (ushort)c);
            }
            else
            {
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 0), (ushort)a);
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 4), (ushort)b);
                BinaryPrimitives.WriteUInt32LittleEndian(destination.Slice(offset + 8), (ushort)c);
            }
        }

        public static byte[] DecodeIndexBuffer(int indexCount, int indexSize, byte[] buffer)
        {
            if (indexCount % 3 != 0)
            {
                throw new ArgumentException("Expected indexCount to be a multiple of 3.");
            }

            if (indexSize != 2 && indexSize != 4)
            {
                throw new ArgumentException("Expected indexSize to be either 2 or 4");
            }

            // the minimum valid encoding is header, 1 byte per triangle and a 16-byte codeaux table
            if (buffer.Length < 1 + (indexCount / 3) + 16)
            {
                throw new ArgumentException("Index buffer is too short.");
            }

            if (buffer[0] != IndexHeader)
            {
                throw new ArgumentException("Incorrect index buffer header.");
            }

            var vertexFifo = new Queue<uint>(16);
            var edgeFifo = new Queue<(uint, uint)>(16);

            var next = 0u;
            var last = 0u;

            var code = new Span<byte>(buffer).Slice(1);
            var data = code.Slice(indexCount / 3);

            var codeauxTable = code.Slice(buffer.Length - 17);

            var destination = new Span<byte>(new byte[indexCount * indexSize]);

            using (var stream = new MemoryStream(data.ToArray()))
            using (var dataReader = new BinaryReader(stream))
            {
                for (var i = 0; i < indexCount; i += 3)
                {
                    var codetri = code[0];
                    code = code.Slice(1);

                    if (codetri < 0xf0)
                    {
                        var fe = codetri >> 4;

                        var (a, b) = edgeFifo.Dequeue();

                        var fec = codetri & 15;

                        if (fec != 15)
                        {
                            var c = fec == 0 ? next : vertexFifo.Dequeue();

                            var fec0 = fec == 0;
                            next += fec0 ? 1u : 0u;

                            WriteTriangle(destination, i, indexSize, a, b, c);

                            PushVertexFifo(vertexFifo, c, fec0);

                            PushEdgeFifo(edgeFifo, c, b);
                            PushEdgeFifo(edgeFifo, a, c);
                        }
                        else
                        {
                            var c = last = DecodeIndex(dataReader, next, last);

                            WriteTriangle(destination, i, indexSize, a, b, c);

                            PushVertexFifo(vertexFifo, c);

                            PushEdgeFifo(edgeFifo, c, b);
                            PushEdgeFifo(edgeFifo, a, c);
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

                            var b = (feb == 0) ? next : vertexFifo.Dequeue();

                            var feb0 = feb == 0 ? 1u : 0u;
                            next += feb0;

                            var c = (fec == 0) ? next : vertexFifo.Dequeue();

                            var fec0 = fec == 0 ? 1u : 0u;
                            next += fec0;

                            WriteTriangle(destination, i, indexSize, a, b, c);

                            PushVertexFifo(vertexFifo, a);
                            PushVertexFifo(vertexFifo, b, feb0 == 1u);
                            PushVertexFifo(vertexFifo, c, fec0 == 1u);

                            PushEdgeFifo(edgeFifo, b, a);
                            PushEdgeFifo(edgeFifo, c, b);
                            PushEdgeFifo(edgeFifo, a, c);
                        }
                        else
                        {
                            var codeaux = (uint)dataReader.ReadByte();

                            var fea = codetri == 0xfe ? 0 : 15;
                            var feb = codeaux >> 4;
                            var fec = codeaux & 15;

                            var a = (fea == 0) ? next++ : 0;
                            var b = (feb == 0) ? next++ : vertexFifo.Dequeue();
                            var c = (fec == 0) ? next++ : vertexFifo.Dequeue();

                            if (fea == 15)
                            {
                                last = a = DecodeIndex(dataReader, next, last);
                            }

                            if (feb == 15)
                            {
                                last = a = DecodeIndex(dataReader, next, last);
                            }

                            if (fec == 15)
                            {
                                last = a = DecodeIndex(dataReader, next, last);
                            }

                            WriteTriangle(destination, i, indexSize, a, b, c);

                            PushVertexFifo(vertexFifo, a);
                            PushVertexFifo(vertexFifo, b, (feb == 0) || (feb == 15));
                            PushVertexFifo(vertexFifo, b, (fec == 0) || (fec == 15));

                            PushEdgeFifo(edgeFifo, b, a);
                            PushEdgeFifo(edgeFifo, c, b);
                            PushEdgeFifo(edgeFifo, a, c);
                        }
                    }
                }
            }

            return destination.ToArray();
        }
    }
}

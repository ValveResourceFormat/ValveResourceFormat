using System;
using System.CodeDom.Compiler;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        public List<VertexBuffer> VertexBuffers = new List<VertexBuffer>();
        public List<IndexBuffer> IndexBuffers = new List<IndexBuffer>();

        public struct VertexBuffer
        {
            public uint Count;
            public uint Size;
            public List<VertexAttribute> Attributes;
            public byte[] Buffer;
            public List<Vector3> Tangents;
        }

        public struct VertexAttribute
        {
            public string Name;
            public DXGI_FORMAT Type;
            public uint Offset;
        }

        public struct IndexBuffer
        {
            public uint Count;
            public uint Size;
            public byte[] Buffer;
        }

        // TEMPORARY
        public struct Vector3
        {
            public float x;
            public float y;
            public float z;
        }

        public override BlockType GetChar()
        {
            return BlockType.VBIB;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            //var objsw = new StreamWriter(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Desktop), "test.obj"));

            reader.BaseStream.Position = Offset;

            var vertexOffset = reader.ReadUInt32();
            var vertexCount = reader.ReadUInt32();

            reader.BaseStream.Position = Offset + vertexOffset;
            for (var i = 0; i < vertexCount; i++)
            {
                var vertexBuffer = new VertexBuffer();

                vertexBuffer.Count = reader.ReadUInt32();            //0
                vertexBuffer.Size = reader.ReadUInt32();             //4

                //objsw.WriteLine(string.Format("# Vertex Buffer {0}. Count: {1}, Size: {2}", i, vertexBuffer.Count, vertexBuffer.Size));

                var refA = reader.BaseStream.Position;
                var attributeOffset = reader.ReadUInt32();  //8
                var attributeCount = reader.ReadUInt32();   //12

                //TODO: Read attributes in the future
                var refB = reader.BaseStream.Position;
                var dataOffset = reader.ReadUInt32();       //16
                var totalSize = reader.ReadUInt32();        //20

                vertexBuffer.Attributes = new List<VertexAttribute>();

                reader.BaseStream.Position = refA + attributeOffset;
                for (var j = 0; j < attributeCount; j++)
                {
                    var previousPosition = reader.BaseStream.Position;

                    var attribute = new VertexAttribute();

                    attribute.Name = reader.ReadNullTermString(Encoding.UTF8);

                    // Offset is always 40 bytes from the start
                    reader.BaseStream.Position = previousPosition + 36;

                    attribute.Type = (DXGI_FORMAT)reader.ReadUInt32();
                    attribute.Offset = reader.ReadUInt32();

                    //Console.WriteLine("VB" + i + " " + attribute.Name + " " + attribute.Offset + " (" + attribute.Type + ")");

                    // There's unusual amount of padding in attributes
                    reader.BaseStream.Position = previousPosition + 56;

                    vertexBuffer.Attributes.Add(attribute);
                }

                reader.BaseStream.Position = refB + dataOffset;

                vertexBuffer.Buffer = reader.ReadBytes((int)vertexBuffer.Count * (int)vertexBuffer.Size);
                vertexBuffer.Tangents = new List<Vector3>();
                reader.BaseStream.Position = refB + dataOffset;
                for (var j = 0; j < vertexBuffer.Count; j++)
                {
                    foreach (var attribute in vertexBuffer.Attributes)
                    {
                        switch (attribute.Name)
                        {
                            case "TANGENT":
                                switch (attribute.Type)
                                {
                                    case DXGI_FORMAT.R32G32B32A32_FLOAT:
                                        reader.BaseStream.Position = (refB + dataOffset) + (j * vertexBuffer.Size) + attribute.Offset;

                                        var tangent = default(Vector3);
                                        tangent.x = reader.ReadSingle();
                                        tangent.y = reader.ReadSingle();
                                        tangent.z = reader.ReadSingle();

                                        vertexBuffer.Tangents.Add(tangent);
                                        break;
                                    default:
                                        throw new Exception("Unsupported tangent format " + attribute.Type);
                                }

                                break;
                        }
                    }
                }

                VertexBuffers.Add(vertexBuffer);

                reader.BaseStream.Position = refB + 4 + 4; //Go back to the vertex array to read the next iteration

                //if(i > 0)break; // TODO: Read only first buffer
            }

            reader.BaseStream.Position = Offset + 4 + 4; //We are back at the header.

            var indexOffset = reader.ReadUInt32();
            var indexCount = reader.ReadUInt32();

            Console.WriteLine("index buffers " + indexCount);

            reader.BaseStream.Position = Offset + 8 + indexOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexCount; i++)
            {
                var indexBuffer = new IndexBuffer();

                indexBuffer.Count = reader.ReadUInt32();        //0
                indexBuffer.Size = reader.ReadUInt32();         //4

                //objsw.WriteLine(string.Format("# Index Buffer {0}. Count: {1}, Size: {2}", i, indexBuffer.Count, indexBuffer.Size));

                var unknown1 = reader.ReadUInt32();     //8
                var unknown2 = reader.ReadUInt32();     //12

                var refC = reader.BaseStream.Position;
                var dataOffset = reader.ReadUInt32();   //16
                var dataSize = reader.ReadUInt32();     //20

                reader.BaseStream.Position = refC + dataOffset;

                indexBuffer.Buffer = reader.ReadBytes((int)indexBuffer.Count * (int)indexBuffer.Size);
                IndexBuffers.Add(indexBuffer);

                reader.BaseStream.Position = refC + dataOffset;

                for (var j = 0; j < indexBuffer.Count; j += 3)
                {
                    var indexOne = reader.ReadUInt16() + 1;
                    var indexTwo = reader.ReadUInt16() + 1;
                    var indexThree = reader.ReadUInt16() + 1;

                    //objsw.WriteLine(string.Format("f {0} {1} {2}", indexOne, indexTwo, indexThree));
                }

                reader.BaseStream.Position = refC + 4 + 4; //Go back to the index array to read the next iteration.

                //if(i > 0)break; // TODO: Read only first buffer
            }

            //objsw.Close();
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            //writer.Write(obj);
            writer.WriteLine("{0:X8}", Offset);
        }
    }
}

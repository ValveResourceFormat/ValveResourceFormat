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
        public List<VertexBuffer> VertexBuffers { get; }
        public List<IndexBuffer> IndexBuffers { get; }

        public struct VertexBuffer
        {
            public uint Count;
            public uint Size;
            public List<VertexAttribute> Attributes;
            public byte[] Buffer;
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

        public VBIB()
        {
            VertexBuffers = new List<VertexBuffer>();
            IndexBuffers = new List<IndexBuffer>();
        }

        public override BlockType GetChar()
        {
            return BlockType.VBIB;
        }

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var vertexOffset = reader.ReadUInt32();
            var vertexCount = reader.ReadUInt32();

            reader.BaseStream.Position = Offset + vertexOffset;
            for (var i = 0; i < vertexCount; i++)
            {
                var vertexBuffer = default(VertexBuffer);

                vertexBuffer.Count = reader.ReadUInt32();            //0
                vertexBuffer.Size = reader.ReadUInt32();             //4

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

                    var attribute = default(VertexAttribute);

                    attribute.Name = reader.ReadNullTermString(Encoding.UTF8);

                    // Offset is always 40 bytes from the start
                    reader.BaseStream.Position = previousPosition + 36;

                    attribute.Type = (DXGI_FORMAT)reader.ReadUInt32();
                    attribute.Offset = reader.ReadUInt32();

                    // There's unusual amount of padding in attributes
                    reader.BaseStream.Position = previousPosition + 56;

                    vertexBuffer.Attributes.Add(attribute);
                }

                reader.BaseStream.Position = refB + dataOffset;

                vertexBuffer.Buffer = reader.ReadBytes((int)vertexBuffer.Count * (int)vertexBuffer.Size);
                VertexBuffers.Add(vertexBuffer);

                reader.BaseStream.Position = refB + 4 + 4; //Go back to the vertex array to read the next iteration

                //if(i > 0)break; // TODO: Read only first buffer
            }

            reader.BaseStream.Position = Offset + 4 + 4; //We are back at the header.

            var indexOffset = reader.ReadUInt32();
            var indexCount = reader.ReadUInt32();

            reader.BaseStream.Position = Offset + 8 + indexOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexCount; i++)
            {
                var indexBuffer = default(IndexBuffer);

                indexBuffer.Count = reader.ReadUInt32();        //0
                indexBuffer.Size = reader.ReadUInt32();         //4

                var unknown1 = reader.ReadUInt32();     //8
                var unknown2 = reader.ReadUInt32();     //12

                var refC = reader.BaseStream.Position;
                var dataOffset = reader.ReadUInt32();   //16
                var dataSize = reader.ReadUInt32();     //20

                reader.BaseStream.Position = refC + dataOffset;

                indexBuffer.Buffer = reader.ReadBytes((int)indexBuffer.Count * (int)indexBuffer.Size);
                IndexBuffers.Add(indexBuffer);

                reader.BaseStream.Position = refC + 4 + 4; //Go back to the index array to read the next iteration.

                //if(i > 0)break; // TODO: Read only first buffer
            }
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("{0:X8}", Offset);
        }
    }
}

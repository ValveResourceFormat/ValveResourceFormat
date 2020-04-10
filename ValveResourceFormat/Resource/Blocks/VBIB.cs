using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.ThirdParty;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        public override BlockType Type => BlockType.VBIB;

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

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            var vertexBufferOffset = reader.ReadUInt32();
            var vertexBufferCount = reader.ReadUInt32();
            var indexBufferOffset = reader.ReadUInt32();
            var indexBufferCount = reader.ReadUInt32();

            reader.BaseStream.Position = Offset + vertexBufferOffset;
            for (var i = 0; i < vertexBufferCount; i++)
            {
                var vertexBuffer = default(VertexBuffer);

                vertexBuffer.Count = reader.ReadUInt32();            //0
                vertexBuffer.Size = reader.ReadUInt32();             //4
                var decompressedSize = vertexBuffer.Count * vertexBuffer.Size;

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

                    attribute.Name = reader.ReadNullTermString(Encoding.UTF8).ToUpperInvariant();

                    // Offset is always 40 bytes from the start
                    reader.BaseStream.Position = previousPosition + 36;

                    attribute.Type = (DXGI_FORMAT)reader.ReadUInt32();
                    attribute.Offset = reader.ReadUInt32();

                    // There's unusual amount of padding in attributes
                    reader.BaseStream.Position = previousPosition + 56;

                    vertexBuffer.Attributes.Add(attribute);
                }

                reader.BaseStream.Position = refB + dataOffset;

                var vertexBufferBytes = reader.ReadBytes((int)totalSize);
                if (totalSize == decompressedSize)
                {
                    vertexBuffer.Buffer = vertexBufferBytes;
                }
                else
                {
                    vertexBuffer.Buffer = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.Count, (int)vertexBuffer.Size, vertexBufferBytes);
                }

                VertexBuffers.Add(vertexBuffer);

                reader.BaseStream.Position = refB + 4 + 4; //Go back to the vertex array to read the next iteration
            }

            reader.BaseStream.Position = Offset + 8 + indexBufferOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexBufferCount; i++)
            {
                var indexBuffer = default(IndexBuffer);

                indexBuffer.Count = reader.ReadUInt32();        //0
                indexBuffer.Size = reader.ReadUInt32();         //4
                var decompressedSize = indexBuffer.Count * indexBuffer.Size;

                var unknown1 = reader.ReadUInt32();     //8
                var unknown2 = reader.ReadUInt32();     //12

                var refC = reader.BaseStream.Position;
                var dataOffset = reader.ReadUInt32();   //16
                var dataSize = reader.ReadUInt32();     //20

                reader.BaseStream.Position = refC + dataOffset;

                if (dataSize == decompressedSize)
                {
                    indexBuffer.Buffer = reader.ReadBytes((int)dataSize);
                }
                else
                {
                    indexBuffer.Buffer = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.Count, (int)indexBuffer.Size, reader.ReadBytes((int)dataSize));
                }

                IndexBuffers.Add(indexBuffer);

                reader.BaseStream.Position = refC + 4 + 4; //Go back to the index array to read the next iteration.
            }
        }

        public float[] ReadVertexAttribute(int offset, VertexBuffer vertexBuffer, VertexAttribute attribute)
        {
            float[] result;

            offset = (int)(offset * vertexBuffer.Size) + (int)attribute.Offset;

            // Useful reference: https://github.com/apitrace/dxsdk/blob/master/Include/d3dx_dxgiformatconvert.inl
            switch (attribute.Type)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                {
                    result = new float[3];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, result, 0, 12);
                    break;
                }

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                {
                    result = new float[4];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, result, 0, 16);
                    break;
                }

                case DXGI_FORMAT.R16G16_UNORM:
                {
                    var shorts = new ushort[2];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, shorts, 0, 4);

                    result = new[]
                    {
                        shorts[0] / 65535f,
                        shorts[1] / 65535f,
                    };
                    break;
                }

                case DXGI_FORMAT.R16G16_FLOAT:
                {
                    var shorts = new ushort[2];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, shorts, 0, 4);

                    result = new[]
                    {
                        HalfTypeHelper.Convert(shorts[0]),
                        HalfTypeHelper.Convert(shorts[1]),
                    };
                    break;
                }

                case DXGI_FORMAT.R32_FLOAT:
                {
                    result = new float[1];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, result, 0, 4);
                    break;
                }

                case DXGI_FORMAT.R32G32_FLOAT:
                {
                    result = new float[2];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, result, 0, 8);
                    break;
                }

                case DXGI_FORMAT.R16G16B16A16_SINT:
                {
                    var shorts = new short[4];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, shorts, 0, 8);

                    result = new float[4];
                    for (var i = 0; i < 4; i++)
                    {
                        result[i] = shorts[i];
                    }

                    break;
                }

                case DXGI_FORMAT.R8G8B8A8_UINT:
                case DXGI_FORMAT.R8G8B8A8_UNORM:
                {
                    var bytes = new byte[4];
                    Buffer.BlockCopy(vertexBuffer.Buffer, offset, bytes, 0, 4);

                    result = new float[4];
                    for (var i = 0; i < 4; i++)
                    {
                        result[i] = attribute.Type == DXGI_FORMAT.R8G8B8A8_UNORM
                            ? bytes[i] / 255f
                            : bytes[i];
                    }

                    break;
                }

                default:
                    throw new NotImplementedException($"Unsupported \"{attribute.Name}\" DXGI_FORMAT.{attribute.Type}");
            }

            return result;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Vertex buffers:");

            foreach (var vertexBuffer in VertexBuffers)
            {
                writer.WriteLine($"Count: {vertexBuffer.Count}");
                writer.WriteLine($"Size: {vertexBuffer.Size}");

                for (var i = 0; i < vertexBuffer.Attributes.Count; i++)
                {
                    var vertexAttribute = vertexBuffer.Attributes[i];
                    writer.WriteLine($"Attribute[{i}].Name = {vertexAttribute.Name}");
                    writer.WriteLine($"Attribute[{i}].Offset = {vertexAttribute.Offset}");
                    writer.WriteLine($"Attribute[{i}].Type = {vertexAttribute.Type}");
                }

                writer.WriteLine();
            }

            writer.WriteLine();
            writer.WriteLine("Index buffers:");

            foreach (var indexBuffer in IndexBuffers)
            {
                writer.WriteLine($"Count: {indexBuffer.Count}");
                writer.WriteLine($"Size: {indexBuffer.Size}");
                writer.WriteLine();
            }
        }
    }
}

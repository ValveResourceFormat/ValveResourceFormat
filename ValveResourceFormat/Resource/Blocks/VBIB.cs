using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        public override BlockType Type => BlockType.VBIB;

        public List<OnDiskBufferData> VertexBuffers { get; }
        public List<OnDiskBufferData> IndexBuffers { get; }

#pragma warning disable CA1051 // Do not declare visible instance fields
        public struct OnDiskBufferData
        {
            public uint ElementCount;
            //stride for vertices. Type for indices
            public uint ElementSizeInBytes;
            //Vertex attribs. Empty for index buffers
            public List<RenderInputLayoutField> InputLayoutFields;
            public byte[] Data;
        }

        public struct RenderInputLayoutField
        {
            public string SemanticName;
            public int SemanticIndex;
            public DXGI_FORMAT Format;
            public uint Offset;
            public int Slot;
            public RenderSlotType SlotType;
            public int InstanceStepRate;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields

        public VBIB()
        {
            VertexBuffers = new List<OnDiskBufferData>();
            IndexBuffers = new List<OnDiskBufferData>();
        }

        public VBIB(IKeyValueCollection data) : this()
        {
            var vertexBuffers = data.GetArray("m_vertexBuffers");
            foreach (var vb in vertexBuffers)
            {
                var vertexBuffer = BufferDataFromDATA(vb);

                var decompressedSize = vertexBuffer.ElementCount * vertexBuffer.ElementSizeInBytes;
                if (vertexBuffer.Data.Length != decompressedSize)
                {
                    vertexBuffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.ElementCount, (int)vertexBuffer.ElementSizeInBytes, vertexBuffer.Data);
                }
                VertexBuffers.Add(vertexBuffer);
            }
            var indexBuffers = data.GetArray("m_indexBuffers");
            foreach (var ib in indexBuffers)
            {
                var indexBuffer = BufferDataFromDATA(ib);

                var decompressedSize = indexBuffer.ElementCount * indexBuffer.ElementSizeInBytes;
                if (indexBuffer.Data.Length != decompressedSize)
                {
                    indexBuffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.ElementCount, (int)indexBuffer.ElementSizeInBytes, indexBuffer.Data);
                }

                IndexBuffers.Add(indexBuffer);
            }
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
                var vertexBuffer = ReadOnDiskBufferData(reader);

                var decompressedSize = vertexBuffer.ElementCount * vertexBuffer.ElementSizeInBytes;
                if (vertexBuffer.Data.Length != decompressedSize)
                {
                    vertexBuffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)vertexBuffer.ElementCount, (int)vertexBuffer.ElementSizeInBytes, vertexBuffer.Data);
                }

                VertexBuffers.Add(vertexBuffer);
            }

            reader.BaseStream.Position = Offset + 8 + indexBufferOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexBufferCount; i++)
            {
                var indexBuffer = ReadOnDiskBufferData(reader);

                var decompressedSize = indexBuffer.ElementCount * indexBuffer.ElementSizeInBytes;
                if (indexBuffer.Data.Length != decompressedSize)
                {
                    indexBuffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)indexBuffer.ElementCount, (int)indexBuffer.ElementSizeInBytes, indexBuffer.Data);
                }

                IndexBuffers.Add(indexBuffer);
            }
        }

        private static OnDiskBufferData ReadOnDiskBufferData(BinaryReader reader)
        {
            var buffer = default(OnDiskBufferData);

            buffer.ElementCount = reader.ReadUInt32();            //0
            buffer.ElementSizeInBytes = reader.ReadUInt32();      //4

            var refA = reader.BaseStream.Position;
            var attributeOffset = reader.ReadUInt32();  //8
            var attributeCount = reader.ReadUInt32();   //12

            var refB = reader.BaseStream.Position;
            var dataOffset = reader.ReadUInt32();       //16
            var totalSize = reader.ReadInt32();        //20

            buffer.InputLayoutFields = new List<RenderInputLayoutField>();

            reader.BaseStream.Position = refA + attributeOffset;
            for (var j = 0; j < attributeCount; j++)
            {
                var attribute = default(RenderInputLayoutField);

                var previousPosition = reader.BaseStream.Position;
                attribute.SemanticName = reader.ReadNullTermString(Encoding.UTF8).ToUpperInvariant();
                reader.BaseStream.Position = previousPosition + 32; //32 bytes long null-terminated string

                attribute.SemanticIndex = reader.ReadInt32();
                attribute.Format = (DXGI_FORMAT)reader.ReadUInt32();
                attribute.Offset = reader.ReadUInt32();
                attribute.Slot = reader.ReadInt32();
                attribute.SlotType = (RenderSlotType)reader.ReadUInt32();
                attribute.InstanceStepRate = reader.ReadInt32();

                buffer.InputLayoutFields.Add(attribute);
            }

            reader.BaseStream.Position = refB + dataOffset;

            buffer.Data = reader.ReadBytes(totalSize); //can be compressed

            reader.BaseStream.Position = refB + 8; //Go back to the index array to read the next iteration.

            return buffer;
        }

        private static OnDiskBufferData BufferDataFromDATA(IKeyValueCollection data)
        {
            OnDiskBufferData buffer = new OnDiskBufferData();
            buffer.ElementCount = data.GetUInt32Property("m_nElementCount");
            buffer.ElementSizeInBytes = data.GetUInt32Property("m_nElementSizeInBytes");

            buffer.InputLayoutFields = new List<RenderInputLayoutField>();

            var inputLayoutFields = data.GetArray("m_inputLayoutFields");
            foreach (var il in inputLayoutFields)
            {
                RenderInputLayoutField attrib = new RenderInputLayoutField();

                //null-terminated string
                attrib.SemanticName = System.Text.Encoding.UTF8.GetString(il.GetArray<byte>("m_pSemanticName")).TrimEnd((char)0);
                attrib.SemanticIndex = il.GetInt32Property("m_nSemanticIndex");
                attrib.Format = (DXGI_FORMAT)il.GetUInt32Property("m_Format");
                attrib.Offset = il.GetUInt32Property("m_nOffset");
                attrib.Slot = il.GetInt32Property("m_nSlot");
                attrib.SlotType = (RenderSlotType)il.GetUInt32Property("m_nSlotType");
                attrib.InstanceStepRate = il.GetInt32Property("m_nInstanceStepRate");

                buffer.InputLayoutFields.Add(attrib);
            }

            buffer.Data = data.GetArray<byte>("m_pData");

            return buffer;
        }

        public static float[] ReadVertexAttribute(int offset, OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            float[] result;

            offset = (int)(offset * vertexBuffer.ElementSizeInBytes) + (int)attribute.Offset;

            // Useful reference: https://github.com/apitrace/dxsdk/blob/master/Include/d3dx_dxgiformatconvert.inl
            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32_FLOAT:
                {
                    result = new float[3];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 12);
                    break;
                }

                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                {
                    result = new float[4];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 16);
                    break;
                }

                case DXGI_FORMAT.R16G16_UNORM:
                {
                    var shorts = new ushort[2];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

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
                    Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

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
                    Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 4);
                    break;
                }

                case DXGI_FORMAT.R32G32_FLOAT:
                {
                    result = new float[2];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, result, 0, 8);
                    break;
                }

                case DXGI_FORMAT.R16G16_SINT:
                {
                    var shorts = new short[2];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 4);

                    result = new float[2];
                    for (var i = 0; i < 2; i++)
                    {
                        result[i] = shorts[i];
                    }

                    break;
                }

                case DXGI_FORMAT.R16G16B16A16_SINT:
                {
                    var shorts = new short[4];
                    Buffer.BlockCopy(vertexBuffer.Data, offset, shorts, 0, 8);

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
                    Buffer.BlockCopy(vertexBuffer.Data, offset, bytes, 0, 4);

                    result = new float[4];
                    for (var i = 0; i < 4; i++)
                    {
                        result[i] = attribute.Format == DXGI_FORMAT.R8G8B8A8_UNORM
                            ? bytes[i] / 255f
                            : bytes[i];
                    }

                    break;
                }

                default:
                    throw new NotImplementedException($"Unsupported \"{attribute.SemanticName}\" DXGI_FORMAT.{attribute.Format}");
            }

            return result;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Vertex buffers:");

            foreach (var vertexBuffer in VertexBuffers)
            {
                writer.WriteLine($"Count: {vertexBuffer.ElementCount}");
                writer.WriteLine($"Size: {vertexBuffer.ElementSizeInBytes}");

                for (var i = 0; i < vertexBuffer.InputLayoutFields.Count; i++)
                {
                    var vertexAttribute = vertexBuffer.InputLayoutFields[i];
                    writer.WriteLine($"Attribute[{ i}]");
                    writer.Indent++;
                    writer.WriteLine($"SemanticName = {vertexAttribute.SemanticName}");
                    writer.WriteLine($"SemanticIndex = {vertexAttribute.SemanticIndex}");
                    writer.WriteLine($"Offset = {vertexAttribute.Offset}");
                    writer.WriteLine($"Format = {vertexAttribute.Format}");
                    writer.WriteLine($"Slot = {vertexAttribute.Slot}");
                    writer.WriteLine($"SlotType = {vertexAttribute.SlotType}");
                    writer.WriteLine($"InstanceStepRate = {vertexAttribute.InstanceStepRate}");
                    writer.Indent--;
                }

                writer.WriteLine();
            }

            writer.WriteLine();
            writer.WriteLine("Index buffers:");

            foreach (var indexBuffer in IndexBuffers)
            {
                writer.WriteLine($"Count: {indexBuffer.ElementCount}");
                writer.WriteLine($"Size: {indexBuffer.ElementSizeInBytes}");
                writer.WriteLine();
            }
        }
    }
}

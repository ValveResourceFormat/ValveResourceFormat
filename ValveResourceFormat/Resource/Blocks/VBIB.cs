using System.Buffers;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;

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
            public RenderInputLayoutField[] InputLayoutFields;
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
            VertexBuffers = [];
            IndexBuffers = [];
        }

        public VBIB(KVObject data) : this()
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

            VertexBuffers.EnsureCapacity((int)vertexBufferCount);
            reader.BaseStream.Position = Offset + vertexBufferOffset;
            for (var i = 0; i < vertexBufferCount; i++)
            {
                var vertexBuffer = ReadOnDiskBufferData(reader, isVertex: true);
                VertexBuffers.Add(vertexBuffer);
            }

            IndexBuffers.EnsureCapacity((int)indexBufferCount);
            reader.BaseStream.Position = Offset + 8 + indexBufferOffset; //8 to take into account vertexOffset / count
            for (var i = 0; i < indexBufferCount; i++)
            {
                var indexBuffer = ReadOnDiskBufferData(reader, isVertex: false);
                IndexBuffers.Add(indexBuffer);
            }
        }

        private static OnDiskBufferData ReadOnDiskBufferData(BinaryReader reader, bool isVertex)
        {
            var buffer = default(OnDiskBufferData);

            buffer.ElementCount = reader.ReadUInt32();            //0
            buffer.ElementSizeInBytes = reader.ReadUInt32();      //4

            // TODO: CS2 hack, figure out what this means
            if ((buffer.ElementSizeInBytes & 0x80000000) != 0)
            {
                buffer.ElementSizeInBytes &= ~0x80000000;
            }

            var refA = reader.BaseStream.Position;
            var attributeOffset = reader.ReadUInt32();  //8
            var attributeCount = reader.ReadUInt32();   //12

            var refB = reader.BaseStream.Position;
            var dataOffset = reader.ReadUInt32();       //16
            var totalSize = reader.ReadInt32();        //20

            reader.BaseStream.Position = refA + attributeOffset;
            buffer.InputLayoutFields = Enumerable.Range(0, (int)attributeCount)
                .Select(j =>
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

                    return attribute;
                })
                .ToArray();

            reader.BaseStream.Position = refB + dataOffset;

            var decompressedSize = (int)(buffer.ElementCount * buffer.ElementSizeInBytes);

            if (totalSize != decompressedSize)
            {
                var temp = ArrayPool<byte>.Shared.Rent(totalSize);

                try
                {
                    var span = temp.AsSpan(0, totalSize);
                    reader.Read(span);

                    if (isVertex)
                    {
                        buffer.Data = MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)buffer.ElementCount, (int)buffer.ElementSizeInBytes, span);
                    }
                    else
                    {
                        buffer.Data = MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)buffer.ElementCount, (int)buffer.ElementSizeInBytes, span);
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }
            else
            {
                buffer.Data = reader.ReadBytes(totalSize);
            }

            reader.BaseStream.Position = refB + 8; //Go back to the index array to read the next iteration.

            return buffer;
        }

        private static OnDiskBufferData BufferDataFromDATA(KVObject data)
        {
            var buffer = new OnDiskBufferData
            {
                ElementCount = data.GetUInt32Property("m_nElementCount"),
                ElementSizeInBytes = data.GetUInt32Property("m_nElementSizeInBytes"),
            };

            var inputLayoutFields = data.GetArray("m_inputLayoutFields");
            buffer.InputLayoutFields = inputLayoutFields.Select(il => new RenderInputLayoutField
            {
                //null-terminated string
                SemanticName = Encoding.UTF8.GetString(il.GetArray<byte>("m_pSemanticName")).TrimEnd((char)0),
                SemanticIndex = il.GetInt32Property("m_nSemanticIndex"),
                Format = (DXGI_FORMAT)il.GetUInt32Property("m_Format"),
                Offset = il.GetUInt32Property("m_nOffset"),
                Slot = il.GetInt32Property("m_nSlot"),
                SlotType = (RenderSlotType)il.GetUInt32Property("m_nSlotType"),
                InstanceStepRate = il.GetInt32Property("m_nInstanceStepRate")
            }).ToArray();

            buffer.Data = data.GetArray<byte>("m_pData");

            return buffer;
        }

        /*
            POSITION - R32G32B32_FLOAT          vec3

            NORMAL - R32_UINT                   compressed
            NORMAL - R32G32B32_FLOAT            vec3
            NORMAL - R8G8B8A8_UNORM             compressed
            TANGENT - R32G32B32A32_FLOAT        vec4

            BLENDINDICES - R16G16_SINT          vec2
            BLENDINDICES - R8G8B8A8_UINT        vec4
            BLENDINDICES - R16G16B16A16_SINT    vec4

            BLENDWEIGHT - R16G16_UNORM          vec2
            BLENDWEIGHT - R8G8B8A8_UNORM        vec4
            BLENDWEIGHTS - R8G8B8A8_UNORM       vec4

            COLOR - R32G32B32A32_FLOAT          vec4
            COLOR - R8G8B8A8_UNORM              vec4

            COLORSET - R32G32B32A32_FLOAT         vec4
            PIVOTPAINT - R32G32B32_FLOAT          vec3
            VERTEXPAINTTINTCOLOR - R8G8B8A8_UNORM vec4

            TEXCOORD - R32_FLOAT               vec1

            TEXCOORD - R16G16_FLOAT            vec2
            TEXCOORD - R16G16_SNORM            vec2
            TEXCOORD - R16G16_UNORM            vec2
            TEXCOORD - R32G32_FLOAT            vec2

            TEXCOORD - R32G32B32_FLOAT         vec3

            TEXCOORD - R32G32B32A32_FLOAT      vec4
            TEXCOORD - R8G8B8A8_UNORM          vec4
            TEXCOORD - R16G16B16A16_FLOAT      vec4
        */

        public static float[] GetScalarAttributeArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            if (attribute.Format != DXGI_FORMAT.R32_FLOAT)
            {
                throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
            }

            var result = new float[vertexBuffer.ElementCount];
            MarshallAttributeArray(result, sizeof(float), vertexBuffer, attribute);
            return result;
        }

        public static Vector2[] GetVector2AttributeArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            var result = new Vector2[vertexBuffer.ElementCount];

            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32_FLOAT:
                    MarshallAttributeArray(result, sizeof(float) * 2, vertexBuffer, attribute);
                    break;

                case DXGI_FORMAT.R16G16_FLOAT:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var halfs = MemoryMarshal.Cast<byte, Half>(data.Slice(offset, 4));
                            result[i] = new Vector2((float)halfs[0], (float)halfs[1]);

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16_UNORM:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var ushorts = MemoryMarshal.Cast<byte, ushort>(data.Slice(offset, 4));
                            result[i] = new Vector2(ushorts[0], ushorts[1]) / 65535f;

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16_SNORM:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var shorts = MemoryMarshal.Cast<byte, short>(data.Slice(offset, 4));
                            result[i] = new Vector2(shorts[0], shorts[1]) / 32767f;

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                default:
                    throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
            }

            return result;
        }

        public static Vector3[] GetVector3AttributeArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            if (attribute.Format != DXGI_FORMAT.R32G32B32_FLOAT)
            {
                throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
            }

            var result = new Vector3[vertexBuffer.ElementCount];
            MarshallAttributeArray(result, sizeof(float) * 3, vertexBuffer, attribute);
            return result;
        }

        public static Vector4[] GetVector4AttributeArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            var result = new Vector4[vertexBuffer.ElementCount];

            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R32G32B32A32_FLOAT:
                    MarshallAttributeArray(result, sizeof(float) * 4, vertexBuffer, attribute);
                    break;

                case DXGI_FORMAT.R16G16B16A16_FLOAT:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var halfs = MemoryMarshal.Cast<byte, Half>(data.Slice(offset, 8));
                            result[i] = new Vector4(
                                (float)halfs[0],
                                (float)halfs[1],
                                (float)halfs[2],
                                (float)halfs[3]
                            );

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            result[i] = new Vector4(
                                data[offset],
                                data[offset + 1],
                                data[offset + 2],
                                data[offset + 3]
                            );

                            result[i] /= 255f;
                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        return result;
                    }

                default:
                    throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
            }

            return result;
        }

        // Tangents array will be empty if it not compressed
        public static (Vector3[] Normals, Vector4[] Tangents) GetNormalTangentArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            if (attribute.Format == DXGI_FORMAT.R32G32B32_FLOAT)
            {
                var normals = new Vector3[vertexBuffer.ElementCount];
                MarshallAttributeArray(normals, sizeof(float) * 3, vertexBuffer, attribute);
                return (normals, Array.Empty<Vector4>());
            }
            else if (attribute.Format == DXGI_FORMAT.R32_UINT) // Version 2 compressed normals (CS2)
            {
                var packedFrames = new uint[vertexBuffer.ElementCount];
                MarshallAttributeArray(packedFrames, sizeof(uint), vertexBuffer, attribute);

                return DecompressNormalTangents2(packedFrames);
            }
            else if (attribute.Format == DXGI_FORMAT.R8G8B8A8_UNORM) // Version 1 compressed normals
            {
                var normals = new Vector3[vertexBuffer.ElementCount];
                var tangents = new Vector4[vertexBuffer.ElementCount];
                var offset = (int)attribute.Offset;

                for (var i = 0; i < vertexBuffer.ElementCount; i++)
                {
                    normals[i] = DecompressNormal(vertexBuffer.Data[offset], vertexBuffer.Data[offset + 1]);
                    tangents[i] = DecompressTangent(vertexBuffer.Data[offset + 2], vertexBuffer.Data[offset + 3]);

                    offset += (int)vertexBuffer.ElementSizeInBytes;
                }

                return (normals, tangents);
            }

            throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
        }

        public static ushort[] GetBlendIndicesArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            const int numJoints = 4;
            var indices = new ushort[vertexBuffer.ElementCount * numJoints];

            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R16G16_SINT:
                    {
                        const int numJointsVbib = 2;

                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var ushorts = MemoryMarshal.Cast<byte, ushort>(data.Slice(offset, numJointsVbib * sizeof(ushort)));

                            System.Diagnostics.Debug.Assert(ushorts[0] <= short.MaxValue);
                            System.Diagnostics.Debug.Assert(ushorts[1] <= short.MaxValue);

                            var fourJoints = indices.AsSpan(i * numJoints, numJoints);
                            fourJoints[0] = ushorts[0];
                            fourJoints[1] = ushorts[1];
                            fourJoints[2] = ushorts[1];
                            fourJoints[3] = ushorts[1];

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16B16A16_SINT:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var ushorts = MemoryMarshal.Cast<byte, ushort>(data.Slice(offset, numJoints * sizeof(ushort)));
                            System.Diagnostics.Debug.Assert(ushorts[0] <= short.MaxValue);
                            System.Diagnostics.Debug.Assert(ushorts[1] <= short.MaxValue);
                            System.Diagnostics.Debug.Assert(ushorts[2] <= short.MaxValue);
                            System.Diagnostics.Debug.Assert(ushorts[3] <= short.MaxValue);

                            ushorts.CopyTo(indices.AsSpan(i * numJoints, numJoints));

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R8G8B8A8_UINT:
                    {
                        var inc = 0;

                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var bytes = data.Slice(offset, 4);
                            System.Diagnostics.Debug.Assert(bytes[0] >= 0);
                            System.Diagnostics.Debug.Assert(bytes[1] >= 0);
                            System.Diagnostics.Debug.Assert(bytes[2] >= 0);
                            System.Diagnostics.Debug.Assert(bytes[3] >= 0);

                            // Note: implicit casting from byte to ushort
                            indices[inc++] = bytes[0];
                            indices[inc++] = bytes[1];
                            indices[inc++] = bytes[2];
                            indices[inc++] = bytes[3];

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }
            }

            return indices;
        }

        public static Vector4[] GetBlendWeightsArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            var weights = new Vector4[vertexBuffer.ElementCount];

            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            weights[i] = new Vector4(
                                data[offset],
                                data[offset + 1],
                                data[offset + 2],
                                data[offset + 3]
                            );

                            weights[i] /= 255f;
                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16_UNORM:
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var packed = Unsafe.ReadUnaligned<uint>(ref MemoryMarshal.GetReference(data[offset..(offset + 4)]));

                            weights[i] = new Vector4(
                                packed & 0x0000FFFF,
                                packed >> 16,
                                0f,
                                0f
                            );

                            weights[i] /= 65535f;
                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }
            }

            return weights;
        }

        private static void MarshallAttributeArray<T>(T[] result, int size, OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            for (var i = 0; i < vertexBuffer.ElementCount; i++)
            {
                result[i] = Unsafe.ReadUnaligned<T>(ref MemoryMarshal.GetReference(data.Slice(offset, size)));

                offset += (int)vertexBuffer.ElementSizeInBytes;
            }
        }

        private static Vector3 DecompressNormal(float x, float y)
        {
            var outputNormal = Vector3.Zero;

            x -= 128.0f;
            y -= 128.0f;
            float z;

            var zSignBit = x < 0 ? 1.0f : 0.0f;    // z and t negative bits (like slt asm instruction)
            var tSignBit = y < 0 ? 1.0f : 0.0f;
            var zSign = -((2 * zSignBit) - 1);     // z and t signs
            var tSign = -((2 * tSignBit) - 1);

            x = (x * zSign) - zSignBit;            // 0..127
            y = (y * tSign) - tSignBit;
            x -= 64;                               // -64..63
            y -= 64;

            var xSignBit = x < 0 ? 1.0f : 0.0f;    // x and y negative bits (like slt asm instruction)
            var ySignBit = y < 0 ? 1.0f : 0.0f;
            var xSign = -((2 * xSignBit) - 1);     // x and y signs
            var ySign = -((2 * ySignBit) - 1);

            x = ((x * xSign) - xSignBit) / 63.0f;  // 0..1 range
            y = ((y * ySign) - ySignBit) / 63.0f;
            z = 1.0f - x - y;

            var oolen = 1.0f / MathF.Sqrt((x * x) + (y * y) + (z * z)); // Normalize and
            x *= oolen * xSign;                   // Recover signs
            y *= oolen * ySign;
            z *= oolen * zSign;

            outputNormal.X = x;
            outputNormal.Y = y;
            outputNormal.Z = z;

            return outputNormal;
        }

        private static Vector4 DecompressTangent(float x, float y)
        {
            var outputNormal = DecompressNormal(x, y);
            var tSign = y < 128.0f ? -1.0f : 1.0f;

            return new Vector4(outputNormal.X, outputNormal.Y, outputNormal.Z, tSign);
        }

        private static (Vector3[] Normals, Vector4[] Tangents) DecompressNormalTangents2(uint[] packedFrames)
        {
            var normals = new Vector3[packedFrames.Length];
            var tangents = new Vector4[packedFrames.Length];

            for (var i = 0; i < packedFrames.Length; i++)
            {
                var nPackedFrame = packedFrames[i];
                var SignBit = nPackedFrame & 1u;            // LSB bit
                float Tbits = (nPackedFrame >> 1) & 0x7ff;  // 11 bits
                float Xbits = (nPackedFrame >> 12) & 0x3ff; // 10 bits
                float Ybits = (nPackedFrame >> 22) & 0x3ff; // 10 bits

                // Unpack from 0..1 to -1..1
                var nPackedFrameX = (Xbits / 1023.0f) * 2.0f - 1.0f;
                var nPackedFrameY = (Ybits / 1023.0f) * 2.0f - 1.0f;

                // Z is never given a sign, meaning negative values are caused by abs(packedframexy) adding up to over 1.0
                var derivedNormalZ = 1.0f - MathF.Abs(nPackedFrameX) - MathF.Abs(nPackedFrameY); // Project onto x+y+z=1
                var unpackedNormal = new Vector3(nPackedFrameX, nPackedFrameY, derivedNormalZ);

                // If Z is negative, X and Y has had extra amounts (TODO: find the logic behind this value) added into them so they would add up to over 1.0
                // Thus, we take the negative components of Z and add them back into XY to get the correct original values.
                var negativeZCompensation = Math.Clamp(-derivedNormalZ, 0.0f, 1.0f); // Isolate the negative 0..1 range of derived Z

                var unpackedNormalXPositive = unpackedNormal.X >= 0.0f ? 1.0f : 0.0f;
                var unpackedNormalYPositive = unpackedNormal.Y >= 0.0f ? 1.0f : 0.0f;

                unpackedNormal.X += negativeZCompensation * (1f - unpackedNormalXPositive) + -negativeZCompensation * unpackedNormalXPositive; // mix() - x×(1−a)+y×a
                unpackedNormal.Y += negativeZCompensation * (1f - unpackedNormalYPositive) + -negativeZCompensation * unpackedNormalYPositive;

                var normal = Vector3.Normalize(unpackedNormal); // Get final normal by normalizing it onto the unit sphere
                normals[i] = normal;

                // Invert tangent when normal Z is negative
                var tangentSign = (normal.Z >= 0.0f) ? 1.0f : -1.0f;
                // equal to tangentSign * (1.0 + abs(normal.z))
                var rcpTangentZ = 1.0f / (tangentSign + normal.Z);

                // Be careful of rearranging ops here, could lead to differences in float precision, especially when dealing with compressed data.
                Vector3 unalignedTangent;

                // Unoptimized (but clean) form:
                // tangent.X = -(normal.x * normal.x) / (tangentSign + normal.z) + 1.0
                // tangent.Y = -(normal.x * normal.y) / (tangentSign + normal.z)
                // tangent.Z = -(normal.x)
                unalignedTangent.X = -tangentSign * (normal.X * normal.X) * rcpTangentZ + 1.0f;
                unalignedTangent.Y = -tangentSign * ((normal.X * normal.Y) * rcpTangentZ);
                unalignedTangent.Z = -tangentSign * normal.X;

                // This establishes a single direction on the tangent plane that derived from only the normal (has no texcoord info).
                // But it doesn't line up with the texcoords. For that, it uses nPackedFrameT, which is the rotation.

                // Angle to use to rotate tangent
                var nPackedFrameT = Tbits / 2047.0f * MathF.Tau;

                // Rotate tangent to the correct angle that aligns with texcoords.
                var tangent = unalignedTangent * MathF.Cos(nPackedFrameT) + Vector3.Cross(normal, unalignedTangent) * MathF.Sin(nPackedFrameT);

                tangents[i] = new Vector4(tangent, (SignBit == 0u) ? -1.0f : 1.0f); // Bitangent sign bit... inverted (0 = negative
            }

            return (normals, tangents);
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Vertex buffers:");

            foreach (var vertexBuffer in VertexBuffers)
            {
                writer.WriteLine($"Count: {vertexBuffer.ElementCount}");
                writer.WriteLine($"Size: {vertexBuffer.ElementSizeInBytes}");

                for (var i = 0; i < vertexBuffer.InputLayoutFields.Length; i++)
                {
                    var vertexAttribute = vertexBuffer.InputLayoutFields[i];
                    writer.WriteLine($"Attribute[{i}]");
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

        public static (int ElementSize, int ElementCount) GetFormatInfo(RenderInputLayoutField attribute)
        {
            return attribute.Format switch
            {
                DXGI_FORMAT.R32G32B32_FLOAT => (4, 3),
                DXGI_FORMAT.R32G32B32A32_FLOAT => (4, 4),
                DXGI_FORMAT.R16G16_UNORM => (2, 2),
                DXGI_FORMAT.R16G16_SNORM => (2, 2),
                DXGI_FORMAT.R16G16_FLOAT => (2, 2),
                DXGI_FORMAT.R32_FLOAT => (4, 1),
                DXGI_FORMAT.R32_UINT => (4, 1),
                DXGI_FORMAT.R32G32_FLOAT => (4, 2),
                DXGI_FORMAT.R16G16_SINT => (2, 2),
                DXGI_FORMAT.R16G16B16A16_SINT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_FLOAT => (2, 4),
                DXGI_FORMAT.R8G8B8A8_UINT => (1, 4),
                DXGI_FORMAT.R8G8B8A8_UNORM => (1, 4),
                _ => throw new NotImplementedException($"Unsupported \"{attribute.SemanticName}\" DXGI_FORMAT.{attribute.Format}"),
            };
        }

        public static int[] CombineRemapTables(int[][] remapTables)
        {
            remapTables = remapTables.Where(remapTable => remapTable.Length != 0).ToArray();
            var newRemapTable = remapTables[0].AsEnumerable();
            for (var i = 1; i < remapTables.Length; i++)
            {
                var remapTable = remapTables[i];
                newRemapTable = newRemapTable.Select(j => j != -1 ? remapTable[j] : -1);
            }
            return newRemapTable.ToArray();
        }

        public VBIB RemapBoneIndices(int[] remapTable)
        {
            var res = new VBIB();
            res.VertexBuffers.AddRange(VertexBuffers.Select(buf =>
            {
                var blendIndices = Array.FindIndex(buf.InputLayoutFields, field => field.SemanticName == "BLENDINDICES");
                if (blendIndices != -1)
                {
                    var field = buf.InputLayoutFields[blendIndices];
                    var (formatElementSize, formatElementCount) = GetFormatInfo(field);
                    var formatSize = formatElementSize * formatElementCount;
                    buf.Data = [.. buf.Data];
                    var bufSpan = buf.Data.AsSpan();
                    var maxRemapTableIdx = remapTable.Length - 1;
                    for (var i = (int)field.Offset; i < buf.Data.Length; i += (int)buf.ElementSizeInBytes)
                    {
                        for (var j = 0; j < formatSize; j += formatElementSize)
                        {
                            switch (formatElementSize)
                            {
                                case 4:
                                    BitConverter.TryWriteBytes(bufSpan[(i + j)..],
                                        remapTable[Math.Min(BitConverter.ToUInt32(buf.Data, i + j), maxRemapTableIdx)]);
                                    break;
                                case 2:
                                    BitConverter.TryWriteBytes(bufSpan[(i + j)..],
                                        (short)remapTable[Math.Min(BitConverter.ToUInt16(buf.Data, i + j), maxRemapTableIdx)]);
                                    break;
                                case 1:
                                    buf.Data[i + j] = (byte)remapTable[Math.Min(buf.Data[i + j], maxRemapTableIdx)];
                                    break;
                                default:
                                    throw new NotImplementedException();
                            }
                        }
                    }
                }
                return buf;
            }));
            res.IndexBuffers.AddRange(IndexBuffers);
            return res;
        }
    }
}

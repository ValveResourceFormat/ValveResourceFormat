using System.Buffers;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;
using ValveResourceFormat.Compression;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.VBIB;

        /// <summary>
        /// Gets the list of vertex buffers.
        /// </summary>
        public List<OnDiskBufferData> VertexBuffers { get; }

        /// <summary>
        /// Gets the list of index buffers.
        /// </summary>
        public List<OnDiskBufferData> IndexBuffers { get; }

#pragma warning disable CA1051 // Do not declare visible instance fields
        /// <summary>
        /// Represents buffer data stored on disk.
        /// </summary>
        public struct OnDiskBufferData
        {
            /// <summary>
            /// Number of elements in the buffer.
            /// </summary>
            public uint ElementCount;

            /// <summary>
            /// Size of each element in bytes. For vertex buffers, this is the stride. For index buffers, this is the type size.
            /// </summary>
            public uint ElementSizeInBytes;

            /// <summary>
            /// Input layout fields describing vertex attributes. Empty for index buffers.
            /// </summary>
            public RenderInputLayoutField[] InputLayoutFields;

            /// <summary>
            /// Raw buffer data.
            /// </summary>
            public byte[] Data;

            /// <summary>
            /// Total size of the buffer in bytes.
            /// </summary>
            public readonly uint TotalSizeInBytes => ElementCount * ElementSizeInBytes;
        }

        /// <summary>
        /// Represents a field in the render input layout.
        /// </summary>
        public struct RenderInputLayoutField
        {
            /// <summary>
            /// Semantic name of the attribute (e.g., "POSITION", "NORMAL", "TEXCOORD").
            /// </summary>
            public string SemanticName;

            /// <summary>
            /// Semantic index for the attribute.
            /// </summary>
            public int SemanticIndex;

            /// <summary>
            /// Data format of the attribute.
            /// </summary>
            public DXGI_FORMAT Format;

            /// <summary>
            /// Byte offset of the attribute within the vertex.
            /// </summary>
            public uint Offset;

            /// <summary>
            /// Input slot index.
            /// </summary>
            public int Slot;

            /// <summary>
            /// Type of the input slot.
            /// </summary>
            public RenderSlotType SlotType;

            /// <summary>
            /// Number of instances to draw using the same per-instance data before advancing by one element.
            /// </summary>
            public int InstanceStepRate;

            /// <summary>
            /// Shader semantic name.
            /// </summary>
            public string ShaderSemantic;
        }
#pragma warning restore CA1051 // Do not declare visible instance fields

        /// <summary>
        /// Initializes a new instance of the <see cref="VBIB"/> class.
        /// </summary>
        public VBIB()
        {
            VertexBuffers = [];
            IndexBuffers = [];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VBIB"/> class from a resource and KV data.
        /// </summary>
        public VBIB(Resource resource, KVObject data) : this()
        {
            Resource = resource;

            var vertexBuffers = data.GetArray("m_vertexBuffers");
            foreach (var vb in vertexBuffers)
            {
                var vertexBuffer = BufferDataFromDATA(vb, isVertex: true);
                VertexBuffers.Add(vertexBuffer);
            }
            var indexBuffers = data.GetArray("m_indexBuffers");
            foreach (var ib in indexBuffers)
            {
                var indexBuffer = BufferDataFromDATA(ib, isVertex: false);
                IndexBuffers.Add(indexBuffer);
            }
        }

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
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

            buffer.ElementCount = reader.ReadUInt32();

            // meshsystem - look for "SceneSystem/ComputeShaderSkinning" string
            var size = reader.ReadInt32();
            buffer.ElementSizeInBytes = (uint)(size & 0x3FFFFFF);

            var isSizeNegative = size < 0; // TODO: what does this actually indicate? Maybe indicates that it is meshopt compressed?
            var isZstdCompressed = (size & 0x8000000) != 0;
            //var unknownThing = ~(size >> 26); // TODO: What is this for? It's stored as (unknownThing & 1)

            var refA = reader.BaseStream.Position;
            var attributeOffset = reader.ReadUInt32();
            var attributeCount = reader.ReadUInt32();

            var refB = reader.BaseStream.Position;
            var dataOffset = reader.ReadUInt32();
            var totalSize = reader.ReadInt32();

            reader.BaseStream.Position = refA + attributeOffset;
            buffer.InputLayoutFields = new RenderInputLayoutField[(int)attributeCount];

            for (var i = 0; i < buffer.InputLayoutFields.Length; i++)
            {
                var previousPosition = reader.BaseStream.Position;
                var name = reader.ReadNullTermString(Encoding.UTF8).ToUpperInvariant();
                reader.BaseStream.Position = previousPosition + 32; // 32 bytes long null-terminated string

                var attribute = new RenderInputLayoutField
                {
                    SemanticName = name,
                    SemanticIndex = reader.ReadInt32(),
                    Format = (DXGI_FORMAT)reader.ReadUInt32(),
                    Offset = reader.ReadUInt32(),
                    Slot = reader.ReadInt32(),
                    SlotType = (RenderSlotType)reader.ReadUInt32(),
                    InstanceStepRate = reader.ReadInt32(),
                };

                buffer.InputLayoutFields[i] = attribute;
            }

            reader.BaseStream.Position = refB + dataOffset;

            var decompressedSize = (int)buffer.TotalSizeInBytes;

            if (decompressedSize > totalSize)
            {
                var temp = ArrayPool<byte>.Shared.Rent(totalSize);

                try
                {
                    var span = temp.AsSpan(0, totalSize);
                    reader.Read(span);

                    buffer.Data = DecompressData(buffer, span, decompressedSize, isVertex, isZstdCompressed);
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

        private static byte[] DecompressData(OnDiskBufferData buffer, Span<byte> span, int decompressedSize, bool isVertex, bool isZstdCompressed)
        {
            byte[]? tempZstd = null;

            try
            {
                if (isZstdCompressed)
                {
                    using var zstdDecompressor = new ZstdSharp.Decompressor();

                    // There is no expected decompressed size, so we just use buffer size for fully decoded vertex buffer
                    // and then use the return value of zstd decompress to pass into the vertex decoder as the buffer size
                    tempZstd = ArrayPool<byte>.Shared.Rent(decompressedSize);

                    if (!zstdDecompressor.TryUnwrap(span, tempZstd, out var written))
                    {
                        throw new InvalidDataException("Failed to decompress ZSTD.");
                    }

                    span = tempZstd.AsSpan(0, written);
                }

                if (isVertex)
                {
                    return MeshOptimizerVertexDecoder.DecodeVertexBuffer((int)buffer.ElementCount, (int)buffer.ElementSizeInBytes, span);
                }
                else
                {
                    return MeshOptimizerIndexDecoder.DecodeIndexBuffer((int)buffer.ElementCount, (int)buffer.ElementSizeInBytes, span);
                }
            }
            finally
            {
                if (tempZstd != null)
                {
                    ArrayPool<byte>.Shared.Return(tempZstd);
                }
            }
        }

        private OnDiskBufferData BufferDataFromDATA(KVObject data, bool isVertex)
        {
            var buffer = new OnDiskBufferData
            {
                ElementCount = data.GetUInt32Property("m_nElementCount"),
                ElementSizeInBytes = data.GetUInt32Property("m_nElementSizeInBytes"),
            };

            var inputLayoutFields = data.GetArray("m_inputLayoutFields");
            buffer.InputLayoutFields = [.. inputLayoutFields.Select(static il =>
            {
                var semanticName = il.Properties["m_pSemanticName"];
                var semanticNameStr = string.Empty;

                if (semanticName.Value is string str)
                {
                    semanticNameStr = str;
                }
                else if (semanticName.Value is byte[] bytes)
                {
                    semanticNameStr = Encoding.UTF8.GetString(bytes.AsSpan().TrimEnd((byte)0));
                }
                else
                {
                    Debug.Assert(false);
                }

                return new RenderInputLayoutField
                {
                    SemanticName = semanticNameStr.ToUpperInvariant(),
                    SemanticIndex = il.GetInt32Property("m_nSemanticIndex"),
                    Format = (DXGI_FORMAT)il.GetUInt32Property("m_Format"),
                    Offset = il.GetUInt32Property("m_nOffset"),
                    Slot = il.GetInt32Property("m_nSlot"),
                    SlotType = il.GetEnumValue<RenderSlotType>("m_nSlotType"),
                    InstanceStepRate = il.GetInt32Property("m_nInstanceStepRate"),
                    ShaderSemantic = il.GetStringProperty("m_szShaderSemantic"),
                };
            })];

            if (data.ContainsKey("m_pData"))
            {
                var bufferData = data.GetArray<byte>("m_pData");
                var decompressedSize = (int)buffer.TotalSizeInBytes;

                buffer.Data = bufferData.Length == decompressedSize
                    ? bufferData
                    : DecompressData(buffer, bufferData, decompressedSize, isVertex, isZstdCompressed: false);
            }
            else // MVTX MIDX update
            {
                var blockIndex = data.GetInt32Property("m_nBlockIndex");
                var dataBlock = Resource.GetBlockByIndex(blockIndex);
                var isMeshoptCompressed = data.GetByteProperty("m_bMeshoptCompressed") == 1;
                var isZstdCompressed = data.GetByteProperty("m_bCompressedZSTD") == 1;
                var compressedSize = (int)dataBlock.Size;

                var temp = ArrayPool<byte>.Shared.Rent(compressedSize);

                try
                {
                    Debug.Assert(Resource.Reader != null);
                    var span = temp.AsSpan(0, compressedSize);
                    Resource.Reader.BaseStream.Position = dataBlock.Offset;
                    Resource.Reader.Read(span);

                    if (isMeshoptCompressed)
                    {
                        buffer.Data = DecompressData(buffer, span, (int)buffer.TotalSizeInBytes, isVertex, isZstdCompressed);
                    }
                    else
                    {
                        buffer.Data = span.ToArray();
                    }
                }
                finally
                {
                    ArrayPool<byte>.Shared.Return(temp);
                }
            }

            return buffer;
        }

        /*
            :VertexAttributeFormat


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
            BLENDWEIGHT - R16G16B16A16_UNORM    vec4
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

        /// <summary>
        /// Extracts scalar (single float) attribute data from a vertex buffer.
        /// </summary>
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

        /// <summary>
        /// Extracts 2D vector attribute data from a vertex buffer.
        /// </summary>
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

        /// <summary>
        /// Extracts 3D vector attribute data from a vertex buffer.
        /// </summary>
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

        /// <summary>
        /// Extracts 4D vector attribute data from a vertex buffer.
        /// </summary>
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

        /// <summary>
        /// Extracts normal and tangent data from a vertex buffer. Tangent array will be empty if normals are not compressed.
        /// </summary>
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

        /// <summary>
        /// Extracts blend indices from a vertex buffer, optionally remapping them using the provided table.
        /// </summary>
        public static ushort[] GetBlendIndicesArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute, int[]? remapTable = null)
        {
            var numJoints = attribute.Format is DXGI_FORMAT.R32G32B32A32_SINT or DXGI_FORMAT.R16G16B16A16_UINT ? 8 : 4;
            var indices = new ushort[vertexBuffer.ElementCount * numJoints];

            var offset = (int)attribute.Offset;
            ReadOnlySpan<byte> data = vertexBuffer.Data.AsSpan();

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
                case DXGI_FORMAT.R32G32B32A32_SINT: // 8 joints
                    {
                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var ushorts = MemoryMarshal.Cast<byte, ushort>(data.Slice(offset, numJoints * sizeof(ushort)));
#if DEBUG
                            for (var j = 0; j < numJoints; j++)
                            {
                                System.Diagnostics.Debug.Assert(ushorts[j] <= short.MaxValue);
                            }
#endif

                            ushorts.CopyTo(indices.AsSpan(i * numJoints, numJoints));
                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R8G8B8A8_UINT:
                case DXGI_FORMAT.R16G16B16A16_UINT: // 8 joints
                    {
                        var inc = 0;

                        for (var i = 0; i < vertexBuffer.ElementCount; i++)
                        {
                            var bytes = data.Slice(offset, numJoints);

                            for (var j = 0; j < numJoints; j++)
                            {
                                System.Diagnostics.Debug.Assert(bytes[j] >= 0);
                                indices[inc++] = bytes[j];
                            }

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                default:
                    throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
            }

            if (remapTable != null)
            {
                for (var i = 0; i < indices.Length; i++)
                {
                    indices[i] = checked((ushort)remapTable[indices[i]]);
                }
            }

            return indices;
        }

        /// <summary>
        /// Extracts blend weights from a vertex buffer.
        /// </summary>
        public static Vector4[] GetBlendWeightsArray(OnDiskBufferData vertexBuffer, RenderInputLayoutField attribute)
        {
            var numVectors = attribute.Format is DXGI_FORMAT.R16G16B16A16_UNORM ? 2 : 1;
            var weights = new Vector4[vertexBuffer.ElementCount * numVectors];

            var offset = (int)attribute.Offset;
            var data = vertexBuffer.Data.AsSpan();

            switch (attribute.Format)
            {
                case DXGI_FORMAT.R8G8B8A8_UNORM:
                    {
                        for (var i = 0; i < weights.Length; i++)
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

                case DXGI_FORMAT.R16G16B16A16_UNORM:
                    {
                        for (var i = 0; i < weights.Length - 1; i += 2)
                        {
                            weights[i] = new Vector4(data[offset], data[offset + 1], data[offset + 2], data[offset + 3]) / 255f;
                            weights[i + 1] = new Vector4(data[offset + 4], data[offset + 5], data[offset + 6], data[offset + 7]) / 255f;

                            offset += (int)vertexBuffer.ElementSizeInBytes;
                        }

                        break;
                    }

                case DXGI_FORMAT.R16G16_UNORM:
                    {
                        for (var i = 0; i < weights.Length; i++)
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

                default:
                    throw new InvalidDataException($"Unexpected {attribute.SemanticName} attribute format {attribute.Format}");
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

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        /// <remarks>
        /// Outputs information about vertex and index buffers including their attributes and formats.
        /// </remarks>
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

        /// <summary>
        /// Gets the element size and count for a given render input layout field format.
        /// </summary>
        public static (int ElementSize, int ElementCount) GetFormatInfo(RenderInputLayoutField attribute)
        {
            // :VertexAttributeFormat - When adding new attribute here, also implement it in the renderer - GPUMeshBufferCache
            return attribute.Format switch
            {
                DXGI_FORMAT.R8G8B8A8_UINT => (1, 4),
                DXGI_FORMAT.R8G8B8A8_UNORM => (1, 4),

                DXGI_FORMAT.R16G16_FLOAT => (2, 2),
                DXGI_FORMAT.R16G16_SINT => (2, 2),
                DXGI_FORMAT.R16G16_SNORM => (2, 2),
                DXGI_FORMAT.R16G16_UNORM => (2, 2),

                DXGI_FORMAT.R16G16B16A16_FLOAT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_SINT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_UINT => (2, 4),
                DXGI_FORMAT.R16G16B16A16_UNORM => (2, 4),

                DXGI_FORMAT.R32_FLOAT => (4, 1),
                DXGI_FORMAT.R32_UINT => (4, 1),
                DXGI_FORMAT.R32G32_FLOAT => (4, 2),
                DXGI_FORMAT.R32G32B32_FLOAT => (4, 3),
                DXGI_FORMAT.R32G32B32A32_FLOAT => (4, 4),
                DXGI_FORMAT.R32G32B32A32_SINT => (4, 4),

                _ => throw new NotImplementedException($"Unsupported \"{attribute.SemanticName}\" DXGI_FORMAT.{attribute.Format}"),
            };
        }
    }
}

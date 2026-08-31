using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.InteropServices;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Represents data for a static shader combination.
    /// </summary>
    public sealed class VfxStaticComboData
    {
        /// <summary>Gets the parent program data or null after disposal.</summary>
        public VfxProgramData? ParentProgramData { get; private set; }

        /// <summary>Gets the static combo identifier.</summary>
        public long StaticComboId { get; }

        /// <summary>Gets the variable write sequence shared by all dynamic combos of this static combo.</summary>
        public VfxVariableIndexArray AllVariables { get; }

        /// <summary>Gets the shader attributes.</summary>
        public VfxShaderAttribute[] Attributes { get; } = [];

        /// <summary>Gets the vertex shader input signature indices, one entry per dynamic combo, indexing <see cref="VfxProgramData.VsInputSignatures"/>.</summary>
        public int[] VsInputSignatureIndices { get; } = [];

        /// <summary>Gets the variable write sequences, one per dynamic combo.</summary>
        public VfxVariableIndexArray[] DynamicComboVariables { get; } = [];

        /// <summary>Gets the constant buffer binding slots.</summary>
        public byte[] ConstantBufferBindingSlots { get; } = [];

        /// <summary>Gets the constant buffer binding flags.</summary>
        public byte[] ConstantBufferBindingFlags { get; } = [];

        /// <summary>Gets the constant buffer size.</summary>
        public int ConstantBufferSize { get; }

        /// <summary>Gets whether a static constant buffer is used.</summary>
        public bool StaticCB { get; }

        /// <summary>Gets the globals buffer device address flag. Not seen set in shipped files.</summary>
        public bool GlobalsBDA { get; }

        /// <summary>
        /// Gets whether the shader files were produced by the GLSL based compiler backends
        /// (PCGL, MOBILE_GLES, and early Vulkan). False for D3D and current Vulkan files.
        /// </summary>
        public bool UsesGlslSources { get; }

        /// <summary>Gets the shader files for this combo.</summary>
        public VfxShaderFile[] ShaderFiles { get; } = [];

        /// <summary>Gets the dynamic combos render state info.</summary>
        public VfxRenderStateInfo[] DynamicComboRenderStates { get; } = [];

        // Binary vcs stores the per dynamic combo arrays densely over the entire dynamic combo id space,
        // so the id is already the index. Resource (kv3) shaders only store the combos that exist,
        // addressed by position, and their ids are sparse (e.g. 0..9, 20, 25).
        private readonly Dictionary<long, int>? dynamicComboIdToIndex;

        /// <summary>
        /// Gets the index to address the per dynamic combo arrays with (<see cref="DynamicComboVariables"/>,
        /// <see cref="ConstantBufferBindingSlots"/>, <see cref="ConstantBufferBindingFlags"/>).
        /// </summary>
        /// <param name="dynamicComboId">The dynamic combo id, as found on <see cref="VfxRenderStateInfo.DynamicComboId"/>.</param>
        /// <returns>The array index, or -1 when this combo is not present in this static combo.</returns>
        public int GetDynamicComboIndex(long dynamicComboId)
        {
            if (dynamicComboIdToIndex == null)
            {
                return (int)dynamicComboId;
            }

            return dynamicComboIdToIndex.TryGetValue(dynamicComboId, out var index) ? index : -1;
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxStaticComboData"/> class from a KV object.
        /// </summary>
        public VfxStaticComboData(KVObject data, long staticComboId, VfxShaderAttribute[] attributes, IReadOnlyList<KVObject> byteCodeDataArray, VfxProgramData programData)
        {
            ParentProgramData = programData;
            StaticComboId = staticComboId;

            var dynamicComboIds = data.GetIntegerArray("m_dynamicComboIDs"); // This can be empty sometimes?
            var dynamicComboRenderState = data.GetArray("m_dynamicComboRenderState");
            var byteCodeIndex = data.GetArray<int>("m_byteCodeIndex")!;

            DynamicComboRenderStates = new VfxRenderStateInfo[dynamicComboRenderState.Count];
            dynamicComboIdToIndex = new Dictionary<long, int>(DynamicComboRenderStates.Length);

            for (var i = 0; i < DynamicComboRenderStates.Length; i++)
            {
                var id = dynamicComboIds.Length > 0
                    ? dynamicComboIds[i]
                    : i;

                var renderState = dynamicComboRenderState[i];

                DynamicComboRenderStates[i] = programData.VcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState
                        => new VfxRenderStateInfoPixelShader(id, byteCodeIndex[i], -1, renderState, programData.VcsVersion),
                    _ => new VfxRenderStateInfo(id, byteCodeIndex[i], -1),
                };

                dynamicComboIdToIndex[id] = i;
            }

            var byteCodeDataIdx = data.GetInt32Property("m_nByteCodeDataIdx");

            if (byteCodeDataIdx >= 0)
            {
                var byteCodeData = byteCodeDataArray[byteCodeDataIdx];

                var blockOffset = byteCodeData.GetInt32Property("m_nOffs");
                var blockSize = byteCodeData.GetInt32Property("m_nSize");
                var finalOffset = programData.Resource!.FileSize + blockOffset;

                programData.DataReader!.BaseStream.Position = finalOffset;

                using var byteCodeStream = VfxStaticComboVcsEntry.GetUncompressedStaticComboDataStream(programData.DataReader, ParentProgramData);
                using var byteCodeReader = new BinaryReader(byteCodeStream, Encoding.UTF8, leaveOpen: true);
                Debug.Assert(programData.DataReader.BaseStream.Position == finalOffset + blockSize);

                var hashes = byteCodeData.GetArray("m_hash");
                var offsets = byteCodeData.GetArray<uint>("m_offs")!;
                Debug.Assert(offsets.Length == hashes.Count + 1);

                ShaderFiles = new VfxShaderFile[hashes.Count];
                foreach (var i in byteCodeIndex)
                {
                    if (i == -1)
                    {
                        continue;
                    }

                    var hash = new Guid(hashes[i].GetArray<byte>("m_nHashChar")!);
                    var byteCodeOffset = offsets[i];
                    var byteCodeSize = offsets[i + 1] - byteCodeOffset;

                    byteCodeReader.BaseStream.Position = byteCodeOffset;
                    ShaderFiles[i] = ParentProgramData.VcsPlatformType switch
                    {
                        VcsPlatformType.VULKAN => new VfxShaderFileVulkan(byteCodeReader, i, (int)byteCodeSize, hash, this),
                        VcsPlatformType.PC => new VfxShaderFileDXBC(byteCodeReader, i, (int)byteCodeSize, hash, this),
                        _ => throw new NotImplementedException($"Unhandled bytecode reader for resource-encoded shader of platform {ParentProgramData.VcsPlatformType}")
                    };

                    // Debug.Assert(ShaderFiles[i].Size == byteCodeSize);
                }
            }

            var dynamicComboVars = data.GetArray<uint>("m_dynamicComboVars");
            var dynamicComboVarsRef = data.GetArray("m_dynamicComboVarsRef");

            DynamicComboVariables = new VfxVariableIndexArray[dynamicComboVarsRef.Count];
            for (var i = 0; i < dynamicComboVarsRef.Count; i++)
            {
                var variableIndexArray = dynamicComboVarsRef[i];
                var start = variableIndexArray.GetInt32Property("m_indexAndRegisterOffsetStart");
                var count = variableIndexArray.GetInt32Property("m_indexAndRegisterOffsetCount");

                if (start <= 0)
                {
                    start = 0; // psrs = -1073741824
                }

                DynamicComboVariables[i] = new VfxVariableIndexArray(
                    dynamicComboVars.AsSpan(start, count),
                    variableIndexArray.GetInt32Property("m_nFirstRenderStateElement"),
                    variableIndexArray.GetInt32Property("m_nFirstConstantElement"),
                    i
                );
            }

            var constantBufferBindingArray = data.GetArray<int>("m_constantBufferBindingArray")!;
            ConstantBufferBindingSlots = [.. constantBufferBindingArray.Select(i => (byte)(i >> 0))];
            ConstantBufferBindingFlags = [.. constantBufferBindingArray.Select(i => (byte)(i >> 8))];

            ConstantBufferSize = data.GetInt32Property("m_nConstantBufferSize");
            StaticCB = data.GetUInt32Property("m_bStaticCB") != 0u;
            GlobalsBDA = data.GetUInt32Property("m_bGlobalsBDA") != 0u;

            var allVars = data.GetSubCollection("m_allVars");
            AllVariables = new VfxVariableIndexArray(
                allVars.GetArray<uint>("m_indexAndRegisterOffsetArray"),
                allVars.GetInt32Property("m_nFirstRenderStateElement"),
                allVars.GetInt32Property("m_nFirstConstantElement"),
                -1
            );

            VsInputSignatureIndices = [.. data.GetIntegerArray("m_vsInputSignatureIndexArray").Select(i => (int)i)];
            Attributes = [.. data.GetIntegerArray("m_attribIdx").Select(i => attributes[i])];
        }

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxStaticComboData"/> class from a stream.
        /// </summary>
        public VfxStaticComboData(Stream stream, long staticComboId, VfxProgramData programData)
        {
            ParentProgramData = programData;
            StaticComboId = staticComboId;
            using var dataReader = new BinaryReader(stream, Encoding.UTF8, leaveOpen: true);

            if (programData.VcsVersion < 62) // not precise
            {
                _ = dataReader.ReadUInt64(); // probably StaticComboId
            }

            AllVariables = new VfxVariableIndexArray(dataReader, -1, readRegisterOffset: ParentProgramData.VcsProgramType != VcsProgramType.Features);

            int attributeCount = dataReader.ReadInt16();
            Attributes = new VfxShaderAttribute[attributeCount];
            for (var i = 0; i < attributeCount; i++)
            {
                VfxShaderAttribute attribute = new(dataReader);
                Attributes[i] = attribute;
            }

            if (ParentProgramData.VcsProgramType is VcsProgramType.Features or VcsProgramType.VertexShader)
            {
                int vsInputSignatureIndexCount = dataReader.ReadInt16();
                VsInputSignatureIndices = new int[vsInputSignatureIndexCount];
                for (var i = 0; i < vsInputSignatureIndexCount; i++)
                {
                    VsInputSignatureIndices[i] = dataReader.ReadInt16();
                }

                if (ParentProgramData.VcsProgramType == VcsProgramType.Features)
                {
                    if (dataReader.BaseStream.Position != dataReader.BaseStream.Length)
                    {
                        throw new ShaderParserException("End of file expected");
                    }

                    return;
                }
            }

            int dynamicComboVariablesCount = dataReader.ReadUInt16();
            DynamicComboVariables = new VfxVariableIndexArray[dynamicComboVariablesCount];
            for (var i = 0; i < dynamicComboVariablesCount; i++)
            {
                VfxVariableIndexArray variableIndexArray = new(dataReader, i, readRegisterOffset: true);
                DynamicComboVariables[i] = variableIndexArray;
            }

            int constantBufferBindingCount = dataReader.ReadUInt16();
            ConstantBufferBindingSlots = new byte[constantBufferBindingCount];
            ConstantBufferBindingFlags = new byte[constantBufferBindingCount];
            for (var i = 0; i < constantBufferBindingCount; i++)
            {
                ConstantBufferBindingSlots[i] = dataReader.ReadByte();
                ConstantBufferBindingFlags[i] = dataReader.ReadByte();
            }

            ConstantBufferSize = dataReader.ReadInt32();
            StaticCB = dataReader.ReadBoolean();
            if (ParentProgramData.VcsVersion >= 66)
            {
                GlobalsBDA = dataReader.ReadByte() != 0;
            }

            var shaderFileCount = dataReader.ReadInt32();
            ShaderFiles = new VfxShaderFile[shaderFileCount];

            if (programData.VcsVersion >= 60) // not present in v59, added by v62
            {
                UsesGlslSources = dataReader.ReadBoolean();
            }

            if (ParentProgramData.VcsPlatformType == VcsPlatformType.PC)
            {
                switch (ParentProgramData.VcsShaderModelType)
                {
                    case VcsShaderModelType._20:
                    case VcsShaderModelType._2b:
                    case VcsShaderModelType._30:
                    case VcsShaderModelType._31:
                        ReadDxilSources(dataReader);
                        break;
                    case VcsShaderModelType._40:
                    case VcsShaderModelType._41:
                    case VcsShaderModelType._50:
                    case VcsShaderModelType._60:
                        ReadDxbcSources(dataReader);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported model type {ParentProgramData.VcsPlatformType} {ParentProgramData.VcsShaderModelType}");
                }
            }
            else
            {
                switch (ParentProgramData.VcsPlatformType)
                {
                    case VcsPlatformType.PCGL:
                    case VcsPlatformType.MOBILE_GLES:
                        ReadGlslSources(dataReader);
                        break;
                    case VcsPlatformType.VULKAN:
                    case VcsPlatformType.ANDROID_VULKAN:
                    case VcsPlatformType.IOS_VULKAN:
                        ReadVulkanSources(dataReader);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported source type {ParentProgramData.VcsPlatformType}");
                }
            }

            var renderStateCount = dataReader.ReadInt32();
            DynamicComboRenderStates = new VfxRenderStateInfo[renderStateCount];
            for (var i = 0; i < renderStateCount; i++)
            {
                var renderState = ParentProgramData.VcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState => new VfxRenderStateInfoPixelShader(dataReader),
                    VcsProgramType.HullShader => new VfxRenderStateInfoHullShader(dataReader),
                    _ => new VfxRenderStateInfo(dataReader),
                };

                DynamicComboRenderStates[i] = renderState;
            }

            if (dataReader.BaseStream.Position != dataReader.BaseStream.Length)
            {
                throw new ShaderParserException("End of file expected");
            }
        }

        private void ReadGlslSources(BinaryReader dataReader)
        {
            for (var shaderFileId = 0; shaderFileId < ShaderFiles.Length; shaderFileId++)
            {
                VfxShaderFileGL glslSource = new(dataReader, shaderFileId, this);
                ShaderFiles[shaderFileId] = glslSource;
            }
        }
        private void ReadDxilSources(BinaryReader dataReader)
        {
            for (var shaderFileId = 0; shaderFileId < ShaderFiles.Length; shaderFileId++)
            {
                VfxShaderFileDXIL dxilSource = new(dataReader, shaderFileId, this);
                ShaderFiles[shaderFileId] = dxilSource;
            }
        }
        private void ReadDxbcSources(BinaryReader dataReader)
        {
            for (var shaderFileId = 0; shaderFileId < ShaderFiles.Length; shaderFileId++)
            {
                VfxShaderFileDXBC dxbcSource = new(dataReader, shaderFileId, this);
                ShaderFiles[shaderFileId] = dxbcSource;
            }
        }

        private void ReadVulkanSources(BinaryReader dataReader)
        {
            var isMobile = ParentProgramData?.VcsPlatformType is VcsPlatformType.ANDROID_VULKAN or VcsPlatformType.IOS_VULKAN;

            for (var shaderFileId = 0; shaderFileId < ShaderFiles.Length; shaderFileId++)
            {
                VfxShaderFileVulkan vulkanSource = new(dataReader, shaderFileId, this, isMobile);
                ShaderFiles[shaderFileId] = vulkanSource;
            }
        }

        /// <summary>
        /// Deduplicates write sequences, returning the unique ones in order of first appearance
        /// along with a map of write sequence indices to sequence IDs (-1 for write sequences without data).
        /// The leading write sequence (always present) is sequence 0 even when it carries no data,
        /// as configurations may refer to it.
        /// </summary>
        public (List<VfxVariableIndexArray> Unique, SortedDictionary<int, int> IndexToSequence) GetWriteSequences()
        {
            List<VfxVariableIndexArray> unique = [AllVariables];
            Dictionary<VfxVariableIndexData[], int> sequenceIds = new(WriteSequenceComparer)
            {
                { AllVariables.Fields, 0 }
            };
            SortedDictionary<int, int> indexToSequence = new()
            {
                { AllVariables.Index, 0 }
            };

            foreach (var writeSequence in DynamicComboVariables)
            {
                if (writeSequence.Fields.Length == 0)
                {
                    indexToSequence.Add(writeSequence.Index, -1);
                    continue;
                }

                if (!sequenceIds.TryGetValue(writeSequence.Fields, out var id))
                {
                    id = unique.Count;
                    sequenceIds.Add(writeSequence.Fields, id);
                    unique.Add(writeSequence);
                }

                indexToSequence.Add(writeSequence.Index, id);
            }

            return (unique, indexToSequence);
        }

        private static readonly EqualityComparer<VfxVariableIndexData[]> WriteSequenceComparer = EqualityComparer<VfxVariableIndexData[]>.Create(
            static (a, b) => MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan())),
            static a =>
            {
                var hash = new HashCode();
                hash.AddBytes(MemoryMarshal.AsBytes(a.AsSpan()));
                return hash.ToHashCode();
            });

        /// <summary>
        /// Returns a string description of all attributes.
        /// </summary>
        public string AttributesStringDescription()
        {
            var attributesString = new StringBuilder();
            foreach (var attribute in Attributes)
            {
                attributesString.Append(attribute);
                attributesString.Append('\n');
            }
            return attributesString.ToString();
        }

        /// <summary>
        /// Clears the parent program data reference, invalidating this combo. Called when it is
        /// evicted from the <see cref="StaticComboCache"/>.
        /// </summary>
        public void DetachFromProgram()
        {
            ParentProgramData = null;
        }
    }
}

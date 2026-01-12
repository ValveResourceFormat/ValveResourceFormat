using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Represents data for a static shader combination.
    /// </summary>
    public class VfxStaticComboData
    {
        /// <summary>Gets the parent program data or null after disposal.</summary>
        public VfxProgramData? ParentProgramData { get; private set; }

        /// <summary>Gets the static combo identifier.</summary>
        public long StaticComboId { get; }

        /// <summary>Gets the variables from the static combo.</summary>
        public VfxVariableIndexArray VariablesFromStaticCombo { get; }

        /// <summary>Gets the shader attributes.</summary>
        public VfxShaderAttribute[] Attributes { get; } = [];

        /// <summary>Gets the vertex shader inputs.</summary>
        public int[] VShaderInputs { get; } = [];

        /// <summary>Gets the dynamic combo variables.</summary>
        public VfxVariableIndexArray[] DynamicComboVariables { get; } = [];

        /// <summary>Gets the constant buffer bind info slots.</summary>
        public byte[] ConstantBufferBindInfoSlots { get; } = [];

        /// <summary>Gets the constant buffer bind info flags.</summary>
        public byte[] ConstantBufferBindInfoFlags { get; } = [];

        /// <summary>Gets the constant buffer size.</summary>
        public int ConstantBufferSize { get; }

        /// <summary>Gets whether the first constant-buffer flag is set.</summary>
        public bool Flagbyte0 { get; }

        /// <summary>Gets the second flag byte.</summary>
        public byte Flagbyte1 { get; }

        /// <summary>Gets whether the third flag is set.</summary>
        public bool Flagbyte2 { get; }

        /// <summary>Gets the shader files for this combo.</summary>
        public VfxShaderFile[] ShaderFiles { get; } = [];

        /// <summary>Gets the dynamic combos render state info.</summary>
        public VfxRenderStateInfo[] DynamicCombos { get; } = [];

        /// <summary>
        /// Initializes a new instance of the <see cref="VfxStaticComboData"/> class from a KV object.
        /// </summary>
        public VfxStaticComboData(KVObject data, long staticComboId, VfxShaderAttribute[] attributes, KVObject[] byteCodeDataArray, VfxProgramData programData)
        {
            ParentProgramData = programData;
            StaticComboId = staticComboId;

            var dynamicComboIds = data.GetIntegerArray("m_dynamicComboIDs"); // This can be empty sometimes?
            var dynamicComboRenderState = data.GetArray("m_dynamicComboRenderState");
            var byteCodeIndex = data.GetArray<int>("m_byteCodeIndex")!;

            DynamicCombos = new VfxRenderStateInfo[dynamicComboRenderState.Length];
            for (var i = 0; i < DynamicCombos.Length; i++)
            {
                var id = dynamicComboIds.Length > 0
                    ? dynamicComboIds[i]
                    : i;

                var renderState = dynamicComboRenderState[i];

                DynamicCombos[i] = programData.VcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState
                        => new VfxRenderStateInfoPixelShader(i, byteCodeIndex[i], -1, renderState),
                    _ => new VfxRenderStateInfo(i, byteCodeIndex[i], -1),
                };
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
                Debug.Assert(offsets.Length == hashes.Length + 1);

                ShaderFiles = new VfxShaderFile[hashes.Length];
                foreach (var i in byteCodeIndex)
                {
                    var hash = new Guid(hashes[i].GetArray<byte>("m_nHashChar")!);
                    var byteCodeOffset = offsets[i];
                    var byteCodeSize = offsets[i + 1];

                    byteCodeReader.BaseStream.Position = byteCodeOffset;
                    ShaderFiles[i] = ParentProgramData.VcsPlatformType switch
                    {
                        VcsPlatformType.VULKAN => new VfxShaderFileVulkan(byteCodeReader, i, hash, this),
                        VcsPlatformType.PC => new VfxShaderFileDXBC(byteCodeReader, i, (int)byteCodeSize, hash, this),
                        _ => throw new NotImplementedException($"Unhandled bytecode reader for resource-encoded shader of platform {ParentProgramData.VcsPlatformType}")
                    };

                    // Debug.Assert(ShaderFiles[i].Size == byteCodeSize);
                }
            }

            var dynamicComboVars = data.GetArray<uint>("m_dynamicComboVars");
            var dynamicComboVarsRef = data.GetArray("m_dynamicComboVarsRef");

            DynamicComboVariables = new VfxVariableIndexArray[dynamicComboVarsRef.Length];
            for (var i = 0; i < dynamicComboVarsRef.Length; i++)
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
            ConstantBufferBindInfoSlots = [.. constantBufferBindingArray.Select(i => (byte)(i >> 0))];
            ConstantBufferBindInfoFlags = [.. constantBufferBindingArray.Select(i => (byte)(i >> 8))];

            ConstantBufferSize = data.GetInt32Property("m_nConstantBufferSize");
            // todo: are these correct?
            Flagbyte0 = data.GetUInt32Property("m_bStaticCB") != 0u;
            Flagbyte1 = (byte)data.GetUInt32Property("m_bGlobalsBDA"); //  != 0u

            var allVars = data.GetSubCollection("m_allVars");
            VariablesFromStaticCombo = new VfxVariableIndexArray(
                allVars.GetArray<uint>("m_indexAndRegisterOffsetArray"),
                allVars.GetInt32Property("m_nFirstRenderStateElement"),
                allVars.GetInt32Property("m_nFirstConstantElement"),
                -1
            );

            VShaderInputs = [.. data.GetIntegerArray("m_vsInputSignatureIndexArray").Select(i => (int)i)];
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
                var unk1 = dataReader.ReadUInt64(); // probably StaticComboId
            }

            VariablesFromStaticCombo = new VfxVariableIndexArray(dataReader, -1, ParentProgramData.VcsProgramType != VcsProgramType.Features);

            int attributeCount = dataReader.ReadInt16();
            Attributes = new VfxShaderAttribute[attributeCount];
            for (var i = 0; i < attributeCount; i++)
            {
                VfxShaderAttribute attribute = new(dataReader);
                Attributes[i] = attribute;
            }

            if (ParentProgramData.VcsProgramType is VcsProgramType.Features or VcsProgramType.VertexShader)
            {
                int vsInputBlockCount = dataReader.ReadInt16();
                VShaderInputs = new int[vsInputBlockCount];
                for (var i = 0; i < vsInputBlockCount; i++)
                {
                    VShaderInputs[i] = dataReader.ReadInt16();
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

            int dataBlockCount = dataReader.ReadUInt16();
            DynamicComboVariables = new VfxVariableIndexArray[dataBlockCount];
            for (var i = 0; i < dataBlockCount; i++)
            {
                VfxVariableIndexArray dataBlock = new(dataReader, i, true);
                DynamicComboVariables[i] = dataBlock;
            }

            int constantBufferBindInfoSize = dataReader.ReadUInt16();
            ConstantBufferBindInfoSlots = new byte[constantBufferBindInfoSize];
            ConstantBufferBindInfoFlags = new byte[constantBufferBindInfoSize];
            for (var i = 0; i < constantBufferBindInfoSize; i++)
            {
                ConstantBufferBindInfoSlots[i] = dataReader.ReadByte();
                ConstantBufferBindInfoFlags[i] = dataReader.ReadByte();
            }

            ConstantBufferSize = dataReader.ReadInt32();
            Flagbyte0 = dataReader.ReadBoolean();
            if (ParentProgramData.VcsVersion >= 66)
            {
                Flagbyte1 = dataReader.ReadByte();
            }

            var gpuSourceCount = dataReader.ReadInt32();
            ShaderFiles = new VfxShaderFile[gpuSourceCount];

            if (programData.VcsVersion >= 60) // not precise
            {
                Flagbyte2 = dataReader.ReadBoolean();
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

            var countRenderStates = dataReader.ReadInt32();
            DynamicCombos = new VfxRenderStateInfo[countRenderStates];
            for (var i = 0; i < countRenderStates; i++)
            {
                var endBlock = ParentProgramData.VcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState => new VfxRenderStateInfoPixelShader(dataReader),
                    VcsProgramType.HullShader => new VfxRenderStateInfoHullShader(dataReader),
                    _ => new VfxRenderStateInfo(dataReader),
                };

                DynamicCombos[i] = endBlock;
            }

            if (dataReader.BaseStream.Position != dataReader.BaseStream.Length)
            {
                throw new ShaderParserException("End of file expected");
            }
        }

        private void ReadGlslSources(BinaryReader dataReader)
        {
            for (var sourceId = 0; sourceId < ShaderFiles.Length; sourceId++)
            {
                VfxShaderFileGL glslSource = new(dataReader, sourceId, this);
                ShaderFiles[sourceId] = glslSource;
            }
        }
        private void ReadDxilSources(BinaryReader dataReader)
        {
            for (var sourceId = 0; sourceId < ShaderFiles.Length; sourceId++)
            {
                VfxShaderFileDXIL dxilSource = new(dataReader, sourceId, this);
                ShaderFiles[sourceId] = dxilSource;
            }
        }
        private void ReadDxbcSources(BinaryReader dataReader)
        {
            for (var sourceId = 0; sourceId < ShaderFiles.Length; sourceId++)
            {
                VfxShaderFileDXBC dxbcSource = new(dataReader, sourceId, this);
                ShaderFiles[sourceId] = dxbcSource;
            }
        }

        private void ReadVulkanSources(BinaryReader dataReader)
        {
            var isMobile = ParentProgramData?.VcsPlatformType is VcsPlatformType.ANDROID_VULKAN or VcsPlatformType.IOS_VULKAN;

            for (var sourceId = 0; sourceId < ShaderFiles.Length; sourceId++)
            {
                VfxShaderFileVulkan vulkanSource = new(dataReader, sourceId, this, isMobile);
                ShaderFiles[sourceId] = vulkanSource;
            }
        }

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
        /// Disposes resources by clearing the parent program data reference.
        /// </summary>
        public void Dispose()
        {
            ParentProgramData = null;
        }
    }
}

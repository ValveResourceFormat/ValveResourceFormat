using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader
{
    public class VfxStaticComboData
    {
        public VfxProgramData? ParentProgramData { get; private set; }
        public long StaticComboId { get; }
        public VfxVariableIndexArray VariablesFromStaticCombo { get; }
        public VfxShaderAttribute[] Attributes { get; } = [];
        public int[] VShaderInputs { get; } = [];
        public VfxVariableIndexArray[] DynamicComboVariables { get; } = [];
        public byte[] ConstantBufferBindInfoSlots { get; } = [];
        public byte[] ConstantBufferBindInfoFlags { get; } = [];
        public int Flags0 { get; }
        public bool Flagbyte0 { get; }
        public byte Flagbyte1 { get; }
        public bool Flagbyte2 { get; }
        public VfxShaderFile[] ShaderFiles { get; } = [];
        public VfxRenderStateInfo[] DynamicCombos { get; } = [];

        public VfxStaticComboData(KVObject data, long staticComboId, VfxShaderAttribute[] attributes, KVObject[] byteCodeDataArray, VfxProgramData programData)
        {
            ParentProgramData = programData;
            StaticComboId = staticComboId;

            var dynamicComboIds = data.GetArray<int>("m_dynamicComboIDs");
            var byteCodeIndex = data.GetArray<int>("m_byteCodeIndex")!;

            DynamicCombos = new VfxRenderStateInfo[dynamicComboIds.Length];
            for (var i = 0; i < dynamicComboIds.Length; i++)
            {
                DynamicCombos[i] = new VfxRenderStateInfo(dynamicComboIds[i], byteCodeIndex[i], -1);
            }

            var byteCodeData = byteCodeDataArray[data.GetInt32Property("m_nByteCodeDataIdx")];
            var hashes = byteCodeData.GetArray("m_hash");
            var offsets = byteCodeData.GetArray<uint>("m_offs");

            ShaderFiles = new VfxShaderFileTempKv3[hashes.Length];
            foreach (var i in byteCodeIndex)
            {
                var blockOffset = byteCodeData.GetInt32Property("m_nOffs");
                var blockSize = byteCodeData.GetInt32Property("m_nSize");

                programData.DataReader.BaseStream.Position = blockOffset;
                var actualData = programData.DataReader.ReadBytes(blockSize);
                // todo

                var hash = new Guid(hashes[i].GetArray<byte>("m_nHashChar"));
                var byteCodeOffset = offsets[i];

                ShaderFiles[i] = new VfxShaderFileTempKv3(hash, this);
            }

            DynamicCombos = new VfxRenderStateInfo[dynamicComboIds.Length];
            for (var i = 0; i < dynamicComboIds.Length; i++)
            {
                DynamicCombos[i] = new VfxRenderStateInfo(dynamicComboIds[i], byteCodeIndex[i], -1);
            }

            var dynamicComboVars = data.GetArray<uint>("m_dynamicComboVars");
            var dynamicComboVarsRef = data.GetArray("m_dynamicComboVarsRef");

            DynamicComboVariables = new VfxVariableIndexArray[dynamicComboVarsRef.Length];
            for (var i = 0; i < dynamicComboVarsRef.Length; i++)
            {
                var variableIndexArray = dynamicComboVarsRef[i];
                var start = variableIndexArray.GetInt32Property("m_indexAndRegisterOffsetStart");
                var count = variableIndexArray.GetInt32Property("m_indexAndRegisterOffsetCount");
                DynamicComboVariables[i] = new VfxVariableIndexArray(
                    dynamicComboVars.AsSpan(start, count),
                    variableIndexArray.GetInt32Property("m_nFirstRenderStateElement"),
                    variableIndexArray.GetInt32Property("m_nFirstConstantElement")
                )
                { BlockId = i };
            }

            var constantBufferBindingArray = data.GetArray<int>("m_constantBufferBindingArray");
            // ConstantBufferBindInfoSlots
            // ConstantBufferBindInfoFlags

            var allVars = data.GetSubCollection("m_allVars");
            VariablesFromStaticCombo = new VfxVariableIndexArray(
                allVars.GetArray<uint>("m_indexAndRegisterOffsetArray"),
                allVars.GetInt32Property("m_nFirstRenderStateElement"),
                allVars.GetInt32Property("m_nFirstConstantElement")
            )
            { BlockId = -1 };

            VShaderInputs = [.. data.GetIntegerArray("m_vsInputSignatureIndexArray").Select(i => (int)i)];
            Attributes = [.. data.GetIntegerArray("m_attribIdx").Select(i => attributes[i])];
        }

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

            Flags0 = dataReader.ReadInt32(); // probably size of RenderShaderHandle_t__ or the shader data below
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

        public void Dispose()
        {
            ParentProgramData = null;
        }
    }
}

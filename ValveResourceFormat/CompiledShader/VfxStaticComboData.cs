using System.IO;
using System.Text;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class VfxStaticComboData
    {
        public VfxProgramData ParentProgramData { get; private set; }
        public long ZframeId { get; }
        public VfxVariableIndexArray LeadingData { get; }
        public List<VfxShaderAttribute> Attributes { get; } = [];
        public int[] VShaderInputs { get; } = [];
        public List<VfxVariableIndexArray> DataBlocks { get; } = [];
        public byte[] ConstantBufferBindInfoSlots { get; }
        public byte[] ConstantBufferBindInfoFlags { get; }
        public int Flags0 { get; }
        public bool Flagbyte0 { get; }
        public byte Flagbyte1 { get; }
        public int GpuSourceCount { get; }
        public bool Flagbyte2 { get; }
        public List<VfxShaderFile> GpuSources { get; } = [];
        public List<VfxRenderStateInfo> RenderStateInfos { get; } = [];

        public VfxStaticComboData(Stream stream, long zframeId, VfxProgramData programData)
        {
            ParentProgramData = programData;
            ZframeId = zframeId;
            var dataReader = new ShaderDataReader(stream);

            LeadingData = new VfxVariableIndexArray(dataReader, -1, ParentProgramData.VcsProgramType != VcsProgramType.Features);
            int attributeCount = dataReader.ReadInt16();
            for (var i = 0; i < attributeCount; i++)
            {
                VfxShaderAttribute attribute = new(dataReader);
                Attributes.Add(attribute);
            }
            // this data is applicable to vertex shaders
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

            int dataBlockCount = dataReader.ReadInt16();
            for (var blockId = 0; blockId < dataBlockCount; blockId++)
            {
                VfxVariableIndexArray dataBlock = new(dataReader, blockId, true);
                DataBlocks.Add(dataBlock);
            }

            int constantBufferBindInfoSize = dataReader.ReadInt16();
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

            GpuSourceCount = dataReader.ReadInt32();
            Flagbyte2 = dataReader.ReadBoolean();

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
            for (var i = 0; i < countRenderStates; i++)
            {
                var endBlock = ParentProgramData.VcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState => new VfxRenderStateInfoPixelShader(dataReader),
                    VcsProgramType.HullShader => new VfxRenderStateInfoHullShader(dataReader),
                    _ => new VfxRenderStateInfo(dataReader),
                };

                RenderStateInfos.Add(endBlock);
            }

            if (dataReader.BaseStream.Position != dataReader.BaseStream.Length)
            {
                throw new ShaderParserException("End of file expected");
            }
        }

        private void ReadGlslSources(ShaderDataReader dataReader)
        {
            for (var sourceId = 0; sourceId < GpuSourceCount; sourceId++)
            {
                VfxShaderFileGL glslSource = new(dataReader, sourceId, this);
                GpuSources.Add(glslSource);
            }
        }
        private void ReadDxilSources(ShaderDataReader dataReader)
        {
            for (var sourceId = 0; sourceId < GpuSourceCount; sourceId++)
            {
                VfxShaderFileDXIL dxilSource = new(dataReader, sourceId, this);
                GpuSources.Add(dxilSource);
            }
        }
        private void ReadDxbcSources(ShaderDataReader dataReader)
        {
            for (var sourceId = 0; sourceId < GpuSourceCount; sourceId++)
            {
                VfxShaderFileDXBC dxbcSource = new(dataReader, sourceId, this);
                GpuSources.Add(dxbcSource);
            }
        }
        private void ReadVulkanSources(ShaderDataReader dataReader)
        {
            var isMobile = ParentProgramData.VcsPlatformType is VcsPlatformType.ANDROID_VULKAN or VcsPlatformType.IOS_VULKAN;

            for (var sourceId = 0; sourceId < GpuSourceCount; sourceId++)
            {
                VfxShaderFileVulkan vulkanSource = new(dataReader, sourceId, this, isMobile);
                GpuSources.Add(vulkanSource);
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

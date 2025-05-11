using System.IO;
using System.Text;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class VfxStaticComboData : IDisposable
    {
        public ShaderDataReader DataReader { get; private set; }
        public string FilenamePath { get; }
        public VcsProgramType VcsProgramType { get; }
        public VcsPlatformType VcsPlatformType { get; }
        public VcsShaderModelType VcsShaderModelType { get; }
        public long ZframeId { get; }
        public VfxVariableIndexArray LeadingData { get; }
        public List<VfxShaderAttribute> Attributes { get; } = [];
        internal List<short> attributeBlockLengths { get; } = [];
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

        public VfxStaticComboData(byte[] databytes, string filenamepath, long zframeId, VcsProgramType vcsProgramType,
            VcsPlatformType vcsPlatformType, VcsShaderModelType vcsShaderModelType, int vcsVersion)
        {
            FilenamePath = filenamepath;
            VcsProgramType = vcsProgramType;
            VcsPlatformType = vcsPlatformType;
            VcsShaderModelType = vcsShaderModelType;
            DataReader = new ShaderDataReader(new MemoryStream(databytes));
            ZframeId = zframeId;

            LeadingData = new VfxVariableIndexArray(DataReader, -1, VcsProgramType != VcsProgramType.Features);
            int attributeCount = DataReader.ReadInt16();
            for (var i = 0; i < attributeCount; i++)
            {
                var savedOffset = DataReader.BaseStream.Position;
                VfxShaderAttribute attribute = new(DataReader);
                Attributes.Add(attribute);
                attributeBlockLengths.Add((short)(DataReader.BaseStream.Position - savedOffset));
            }
            // this data is applicable to vertex shaders
            if (VcsProgramType == VcsProgramType.Features || VcsProgramType == VcsProgramType.VertexShader)
            {
                int vsInputBlockCount = DataReader.ReadInt16();
                VShaderInputs = new int[vsInputBlockCount];
                for (var i = 0; i < vsInputBlockCount; i++)
                {
                    VShaderInputs[i] = DataReader.ReadInt16();
                }

                if (VcsProgramType == VcsProgramType.Features)
                {
                    if (DataReader.BaseStream.Position != DataReader.BaseStream.Length)
                    {
                        throw new ShaderParserException("End of file expected");
                    }

                    return;
                }
            }

            int dataBlockCount = DataReader.ReadInt16();
            for (var blockId = 0; blockId < dataBlockCount; blockId++)
            {
                VfxVariableIndexArray dataBlock = new(DataReader, blockId, true);
                DataBlocks.Add(dataBlock);
            }

            int constantBufferBindInfoSize = DataReader.ReadInt16();
            ConstantBufferBindInfoSlots = new byte[constantBufferBindInfoSize];
            ConstantBufferBindInfoFlags = new byte[constantBufferBindInfoSize];
            for (var i = 0; i < constantBufferBindInfoSize; i++)
            {
                ConstantBufferBindInfoSlots[i] = DataReader.ReadByte();
                ConstantBufferBindInfoFlags[i] = DataReader.ReadByte();
            }

            Flags0 = DataReader.ReadInt32(); // probably size of RenderShaderHandle_t__ or the shader data below
            Flagbyte0 = DataReader.ReadBoolean();
            if (vcsVersion >= 66)
            {
                Flagbyte1 = DataReader.ReadByte();
            }

            GpuSourceCount = DataReader.ReadInt32();
            Flagbyte2 = DataReader.ReadBoolean();

            if (vcsPlatformType == VcsPlatformType.PC)
            {
                switch (vcsShaderModelType)
                {
                    case VcsShaderModelType._20:
                    case VcsShaderModelType._2b:
                    case VcsShaderModelType._30:
                    case VcsShaderModelType._31:
                        ReadDxilSources(GpuSourceCount);
                        break;
                    case VcsShaderModelType._40:
                    case VcsShaderModelType._41:
                    case VcsShaderModelType._50:
                    case VcsShaderModelType._60:
                        ReadDxbcSources(GpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported model type {vcsPlatformType} {vcsShaderModelType}");
                }
            }
            else
            {
                switch (vcsPlatformType)
                {
                    case VcsPlatformType.PCGL:
                    case VcsPlatformType.MOBILE_GLES:
                        ReadGlslSources(GpuSourceCount);
                        break;
                    case VcsPlatformType.VULKAN:
                    case VcsPlatformType.ANDROID_VULKAN:
                    case VcsPlatformType.IOS_VULKAN:
                        ReadVulkanSources(GpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported source type {vcsPlatformType}");
                }
            }

            var countRenderStates = DataReader.ReadInt32();
            for (var i = 0; i < countRenderStates; i++)
            {
                var endBlock = vcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState => new VfxRenderStateInfoPixelShader(DataReader),
                    VcsProgramType.HullShader => new VfxRenderStateInfoHullShader(DataReader),
                    _ => new VfxRenderStateInfo(DataReader),
                };

                RenderStateInfos.Add(endBlock);
            }

            if (DataReader.BaseStream.Position != DataReader.BaseStream.Length)
            {
                throw new ShaderParserException("End of file expected");
            }
        }

        private void ReadGlslSources(int glslSourceCount)
        {
            for (var sourceId = 0; sourceId < glslSourceCount; sourceId++)
            {
                VfxShaderFileGL glslSource = new(DataReader, sourceId);
                GpuSources.Add(glslSource);
            }
        }
        private void ReadDxilSources(int dxilSourceCount)
        {
            for (var sourceId = 0; sourceId < dxilSourceCount; sourceId++)
            {
                VfxShaderFileDXIL dxilSource = new(DataReader, sourceId);
                GpuSources.Add(dxilSource);
            }
        }
        private void ReadDxbcSources(int dxbcSourceCount)
        {
            for (var sourceId = 0; sourceId < dxbcSourceCount; sourceId++)
            {
                VfxShaderFileDXBC dxbcSource = new(DataReader, sourceId);
                GpuSources.Add(dxbcSource);
            }
        }
        private void ReadVulkanSources(int vulkanSourceCount)
        {
            var isMobile = VcsPlatformType is VcsPlatformType.ANDROID_VULKAN or VcsPlatformType.IOS_VULKAN;

            for (var sourceId = 0; sourceId < vulkanSourceCount; sourceId++)
            {
                VfxShaderFileVulkan vulkanSource = new(DataReader, sourceId, isMobile);
                GpuSources.Add(vulkanSource);
            }
        }

        public VfxVariableIndexArray GetDataBlock(int blockId)
        {
            return blockId == -1 ? LeadingData : DataBlocks[blockId];
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
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && DataReader != null)
            {
                DataReader.Dispose();
                DataReader = null;
            }
        }
    }
}

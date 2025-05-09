using System.IO;
using System.Text;
using ValveResourceFormat.ThirdParty;
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
        public List<GpuSource> GpuSources { get; } = [];
        public List<EndBlock> EndBlocks { get; } = [];

        private int VcsVersion { get; }

        public VfxStaticComboData(byte[] databytes, string filenamepath, long zframeId, VcsProgramType vcsProgramType,
            VcsPlatformType vcsPlatformType, VcsShaderModelType vcsShaderModelType, int vcsVersion, HandleOutputWrite outputWriter = null)
        {
            FilenamePath = filenamepath;
            VcsProgramType = vcsProgramType;
            VcsPlatformType = vcsPlatformType;
            VcsShaderModelType = vcsShaderModelType;
            VcsVersion = vcsVersion;
            DataReader = new ShaderDataReader(new MemoryStream(databytes), outputWriter);
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
                // TODO: I think its supposed to read uint64, uint, uint here.

                var endBlock = vcsProgramType switch
                {
                    VcsProgramType.PixelShader or VcsProgramType.PixelShaderRenderState => new VfxRenderStateInfo(DataReader),
                    VcsProgramType.HullShader => new HsEndBlock(DataReader),
                    _ => new EndBlock(DataReader),
                };

                EndBlocks.Add(endBlock);
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
                GlslSource glslSource = new(DataReader, sourceId);
                GpuSources.Add(glslSource);
            }
        }
        private void ReadDxilSources(int dxilSourceCount)
        {
            for (var sourceId = 0; sourceId < dxilSourceCount; sourceId++)
            {
                DxilSource dxilSource = new(DataReader, sourceId);
                GpuSources.Add(dxilSource);
            }
        }
        private void ReadDxbcSources(int dxbcSourceCount)
        {
            for (var sourceId = 0; sourceId < dxbcSourceCount; sourceId++)
            {
                DxbcSource dxbcSource = new(DataReader, sourceId);
                GpuSources.Add(dxbcSource);
            }
        }
        private void ReadVulkanSources(int vulkanSourceCount)
        {
            for (var sourceId = 0; sourceId < vulkanSourceCount; sourceId++)
            {
                VulkanSource vulkanSource = new(DataReader, sourceId);
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

        public class VfxShaderAttribute
        {
            public string Name0 { get; }
            public uint Murmur32 { get; }
            public Vfx.Type VfxType { get; }
            public short LinkedParameterIndex { get; }
            public byte[] HeaderCode { get; }
            public int DynExpLen { get; } = -1;
            public byte[] DynExpression { get; }
            public string DynExpEvaluated { get; }
            public object ConstValue { get; }

            public VfxShaderAttribute(ShaderDataReader datareader)
            {
                Name0 = datareader.ReadNullTermString(Encoding.UTF8);
                Murmur32 = datareader.ReadUInt32();
                var murmurCheck = MurmurHash2.Hash(Name0.ToLowerInvariant(), StringToken.MURMUR2SEED);
                if (Murmur32 != murmurCheck)
                {
                    throw new ShaderParserException("Murmur check failed on header name");
                }
                VfxType = (Vfx.Type)datareader.ReadByte();
                LinkedParameterIndex = datareader.ReadInt16();

                if (LinkedParameterIndex != -1)
                {
                    return;
                }

                DynExpLen = datareader.ReadInt32();
                if (DynExpLen > 0)
                {
                    DynExpression = datareader.ReadBytes(DynExpLen);
                    DynExpEvaluated = ParseDynamicExpression(DynExpression);
                    return;
                }

                ConstValue = VfxType switch
                {
                    Vfx.Type.Float => datareader.ReadSingle(),
                    Vfx.Type.Int => datareader.ReadInt32(),
                    Vfx.Type.Bool => datareader.ReadByte() != 0,
                    Vfx.Type.String => datareader.ReadNullTermString(Encoding.UTF8),
                    Vfx.Type.Float2 => new Vector2(datareader.ReadSingle(), datareader.ReadSingle()),
                    Vfx.Type.Float3 => new Vector3(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
                    Vfx.Type.Float4 => new Vector4(datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle(), datareader.ReadSingle()),
                    _ => throw new ShaderParserException($"Unexpected attribute type {VfxType} has a constant value."),
                };

            }

            public override string ToString()
            {
                if (DynExpLen > 0)
                {
                    return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {DynExpEvaluated}";
                }
                else
                {
                    return $"{Name0,-40} 0x{Murmur32:x08}  {VfxType,-15} {LinkedParameterIndex,-3}  {ConstValue}";
                }
            }
        }

        public class EndBlock
        {
            public int BlockIdRef { get; }
            public int Arg0 { get; }
            public int SourceRef { get; }
            public int SourcePointer { get; }
            public EndBlock(ShaderDataReader datareader)
            {
                BlockIdRef = datareader.ReadInt32();
                Arg0 = datareader.ReadInt32();
                SourceRef = datareader.ReadInt32();
                SourcePointer = datareader.ReadInt32();
            }
        }

        public class HsEndBlock : EndBlock
        {
            public byte HullShaderArg { get; }

            public HsEndBlock(ShaderDataReader datareader) : base(datareader)
            {
                HullShaderArg = datareader.ReadByte();
            }
        }

        public class VfxRenderStateInfo : EndBlock
        {
            public bool HasRasterizerState { get; }
            public bool HasStencilState { get; }
            public bool HasBlendState { get; }
            public byte[] RsRasterizerStateDesc { get; }
            public byte[] RsDepthStencilStateDesc { get; }
            public byte[] RsBlendStateDesc { get; }
            public VfxRenderStateInfo(ShaderDataReader datareader) : base(datareader)
            {
                int flag0 = datareader.ReadByte();
                int flag1 = datareader.ReadByte();
                int flag2 = datareader.ReadByte();

                if ((flag0 != 0 && flag0 != 1) || (flag1 != 0 && flag1 != 1) || (flag2 != 0 && flag2 != 1))
                {
                    throw new ShaderParserException("unexpected data");
                }
                HasRasterizerState = flag0 == 0;
                HasStencilState = flag1 == 0;
                HasBlendState = flag2 == 0;

                if (HasRasterizerState)
                {
                    RsRasterizerStateDesc = datareader.ReadBytes(16);
                }
                if (HasStencilState)
                {
                    RsDepthStencilStateDesc = datareader.ReadBytes(20);
                }
                if (HasBlendState)
                {
                    RsBlendStateDesc = datareader.ReadBytes(75);
                }
            }
        }

        public void PrintByteDetail(HandleOutputWrite outputWriter = null)
        {
            if (outputWriter != null)
            {
                DataReader.OutputWriter = outputWriter;
            }
            DataReader.BaseStream.Position = 0;
            if (VcsProgramType == VcsProgramType.Features)
            {
                DataReader.Comment("Zframe byte data (encoding for features files has not been determined)");
                DataReader.ShowBytes((int)DataReader.BaseStream.Length);
                return;
            }

            ShowZDataSection(-1); // leading writeseq
            ShowZFrameHeader();
            // this applies only to vs files (ps, gs and psrs files don't have this section)
            if (VcsProgramType == VcsProgramType.VertexShader)
            {
                var blockCountInput = DataReader.ReadInt16AtPosition();
                DataReader.ShowByteCount("Unknown additional parameters, non 'FF FF' entries point to configurations (block IDs)");
                DataReader.ShowBytes(2, breakLine: false);
                DataReader.TabComment($"nr of data-blocks ({blockCountInput})");
                DataReader.ShowBytes(blockCountInput * 2);
                DataReader.OutputWriteLine("");
            }
            var blockCount = DataReader.ReadInt16AtPosition();
            DataReader.ShowByteCount("Data blocks");
            DataReader.ShowBytes(2, breakLine: false);
            DataReader.TabComment($"nr of data-blocks ({blockCount})");
            DataReader.OutputWriteLine("");
            for (var i = 0; i < blockCount; i++)
            {
                ShowZDataSection(i);
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount("Unknown additional parameters, non 'FF FF' entries point to active block IDs");
            var blockCountOutput = DataReader.ReadInt16AtPosition();
            DataReader.ShowBytes(2, breakLine: false);
            DataReader.TabComment($"nr of data-blocks ({blockCountOutput})", 1);
            DataReader.ShowBytes(blockCountOutput * 2);
            DataReader.BreakLine();
            DataReader.ShowByteCount();
            var flagbyte = DataReader.ReadByteAtPosition();
            DataReader.ShowBytes(1, $"possible control byte ({flagbyte}) or flags ({Convert.ToString(flagbyte, 2).PadLeft(8, '0')})");
            DataReader.ShowBytes(1, "values seen (0,1,2)");
            DataReader.ShowBytes(1, "always 0");
            DataReader.ShowBytes(1, "always 0");
            DataReader.ShowBytes(1, "values seen (0,1)");
            if (VcsVersion >= 66)
            {
                DataReader.ShowBytes(1, "added with v66");
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount($"Start of source section, {DataReader.BaseStream.Position} is " +
                $"the base offset for end-section source pointers");
            var gpuSourceCount = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"gpu source files ({gpuSourceCount})");
            DataReader.ShowBytes(1, "unknown boolean, values seen 0,1", tabLen: 13);
            DataReader.BreakLine();

            if (VcsPlatformType == VcsPlatformType.PC)
            {
                switch (VcsShaderModelType)
                {
                    case VcsShaderModelType._20:
                    case VcsShaderModelType._2b:
                    case VcsShaderModelType._30:
                    case VcsShaderModelType._31:
                        ShowDxilSources(gpuSourceCount);
                        break;
                    case VcsShaderModelType._40:
                    case VcsShaderModelType._41:
                    case VcsShaderModelType._50:
                    case VcsShaderModelType._60:
                        ShowDxbcSources(gpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported model type {VcsPlatformType} {VcsShaderModelType}");
                }
            }
            else
            {
                switch (VcsPlatformType)
                {
                    case VcsPlatformType.PCGL:
                    case VcsPlatformType.MOBILE_GLES:
                        ShowGlslSources(gpuSourceCount);
                        break;
                    case VcsPlatformType.VULKAN:
                    case VcsPlatformType.ANDROID_VULKAN:
                    case VcsPlatformType.IOS_VULKAN:
                        ShowVulkanSources(gpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported platform type {VcsPlatformType}");
                }
            }

            //  End blocks for vs, gs, rtx, cs, ds and hs files
            if (VcsProgramType == VcsProgramType.VertexShader || VcsProgramType == VcsProgramType.GeometryShader ||
                VcsProgramType == VcsProgramType.ComputeShader || VcsProgramType == VcsProgramType.DomainShader ||
                VcsProgramType == VcsProgramType.HullShader || VcsProgramType == VcsProgramType.RaytracingShader)
            {
                ShowZAllEndBlocksTypeVs(hullShader: VcsProgramType == VcsProgramType.HullShader);
                DataReader.BreakLine();
            }
            //  End blocks for ps and psrs files
            if (VcsProgramType == VcsProgramType.PixelShader || VcsProgramType == VcsProgramType.PixelShaderRenderState)
            {
                DataReader.ShowByteCount();
                var nrEndBlocks = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, breakLine: false);
                DataReader.TabComment($"nr of end blocks ({nrEndBlocks})");
                DataReader.OutputWriteLine("");
                for (var i = 0; i < nrEndBlocks; i++)
                {
                    DataReader.ShowByteCount($"End-block[{i}]");
                    var blockId = DataReader.ReadInt16AtPosition();
                    DataReader.ShowBytes(4, breakLine: false);
                    DataReader.TabComment($"blockId ref ({blockId})");
                    DataReader.ShowBytes(4, breakLine: false);
                    DataReader.TabComment("always 0");
                    var sourceReference = DataReader.ReadInt16AtPosition();
                    DataReader.ShowBytes(4, breakLine: false);
                    DataReader.TabComment($"source ref ({sourceReference})");
                    var glslPointer = DataReader.ReadUInt32AtPosition();
                    DataReader.ShowBytes(4, breakLine: false);
                    DataReader.TabComment($"glsl source pointer ({glslPointer})");
                    var hasData0 = DataReader.ReadByte() == 0;
                    var hasData1 = DataReader.ReadByte() == 0;
                    var hasData2 = DataReader.ReadByte() == 0;
                    DataReader.ShowBytes(3, breakLine: false);
                    DataReader.TabComment($"(data0={hasData0}, data1={hasData1}, data2={hasData2})", 7);
                    if (hasData0)
                    {
                        DataReader.OutputWriteLine("// data-section 0");
                        DataReader.ShowBytes(16);
                    }
                    if (hasData1)
                    {
                        DataReader.OutputWriteLine("// data-section 1");
                        DataReader.ShowBytes(20);
                    }
                    if (hasData2)
                    {
                        DataReader.OutputWriteLine("// data-section 2");
                        DataReader.ShowBytes(3);
                        DataReader.ShowBytes(8);
                        DataReader.ShowBytes(64, 32);
                    }
                    DataReader.OutputWriteLine("");
                }
            }
            DataReader.ShowEndOfFile();
        }

        private bool prevBlockWasZero;
        public void ShowZDataSection(int blockId)
        {
            var blockSize = ShowZBlockDataHeader(blockId);
            ShowZBlockDataBody(blockSize);
        }
        public int ShowZBlockDataHeader(int blockId)
        {
            var numFields = DataReader.ReadInt32AtPosition();

            if (blockId != -1 && numFields == 0)
            {
                DataReader.ShowBytes(12, breakLine: false);
                DataReader.TabComment($"data-block[{blockId}] (empty)");
                return 0;
            }
            var comment = "";
            if (blockId == -1)
            {
                comment = $"leading data";
            }
            if (blockId >= 0)
            {
                comment = $"data-block[{blockId}]";
            }
            var blockSize = DataReader.ReadInt32AtPosition();
            if (prevBlockWasZero)
            {
                DataReader.OutputWriteLine("");
            }
            DataReader.ShowByteCount(comment);
            DataReader.ShowBytesWithIntValue();
            DataReader.ShowBytesWithIntValue();
            DataReader.ShowBytesWithIntValue();
            if (blockId == -1 && numFields == 0)
            {
                DataReader.BreakLine();
            }
            return blockSize * 4;
        }
        public void ShowZBlockDataBody(int byteSize)
        {
            if (byteSize == 0)
            {
                prevBlockWasZero = true;
                return;
            }
            else
            {
                prevBlockWasZero = false;
            }
            DataReader.Comment($"{byteSize / 4}*4 bytes");
            DataReader.ShowBytes(byteSize);
            DataReader.BreakLine();
        }
        public void ShowZFrameHeader()
        {
            DataReader.ShowByteCount("Frame header");
            DataReader.ShowBytes(2, breakLine: false);
            DataReader.TabComment($"nr of attributes ({Attributes.Count})");
            DataReader.OutputWriteLine("");

            for (var i = 0; i < Attributes.Count; i++)
            {
                var startOffset = DataReader.BaseStream.Position;
                var attribute = Attributes[i];
                DataReader.ReadNullTermString(Encoding.UTF8);
                DataReader.ShowBytes(4, "murmur32");
                DataReader.ShowBytes(1, $"Type: {attribute.VfxType}");
                DataReader.ShowBytes(2, $"{nameof(VfxShaderAttribute.LinkedParameterIndex)}");
                if (attribute.VfxType == Vfx.Type.Sampler2D)
                {
                    DataReader.BreakLine();
                    continue;
                }

                var dynExpLen = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, $"dynamic expression length = {dynExpLen}");
                if (dynExpLen > 0)
                {
                    ShowDynamicExpression(dynExpLen);
                    DataReader.BreakLine();
                    continue;
                }

                DataReader.Comment("Constant attribute value" + attribute.VfxType);
                DataReader.ShowBytes((int)(startOffset - DataReader.BaseStream.Position + attributeBlockLengths[i]), breakLine: false);
            }

            if (Attributes.Count > 0)
            {
                DataReader.BreakLine();
            }
        }

        const int SOURCE_BYTES_TO_SHOW = 96;
        private void ShowDxilSources(int dxilSourceCount)
        {
            for (var i = 0; i < dxilSourceCount; i++)
            {
                var sourceOffset = DataReader.ReadInt32AtPosition();
                DataReader.ShowByteCount();
                DataReader.ShowBytes(4, $"offset to end of source {sourceOffset} (taken from {DataReader.BaseStream.Position + 4})");
                var additionalSourceBytes = 0;
                if (sourceOffset > 0)
                {
                    DataReader.ShowBytes(4);
                    var unknown_prog_uint16 = (int)DataReader.ReadUInt16AtPosition(2);
                    DataReader.ShowBytes(4, $"({unknown_prog_uint16}) the first ({unknown_prog_uint16} * 4) " +
                        $"bytes look like header data that may need to be processed");
                    DataReader.BreakLine();
                    DataReader.ShowByteCount($"DXIL-SOURCE[{i}]");
                    var sourceSize = sourceOffset - 8;
                    if (unknown_prog_uint16 > 0)
                    {
                        DataReader.ShowBytes(unknown_prog_uint16 * 4);
                    }
                    additionalSourceBytes = sourceSize - unknown_prog_uint16 * 4;
                }
                var endOfSource = (int)DataReader.BaseStream.Position + additionalSourceBytes;
                if (additionalSourceBytes > SOURCE_BYTES_TO_SHOW)
                {
                    DataReader.ShowBytes(SOURCE_BYTES_TO_SHOW, breakLine: false);
                    DataReader.OutputWrite(" ");
                    var remainingBytes = endOfSource - (int)DataReader.BaseStream.Position;
                    if (remainingBytes < 50)
                    {
                        DataReader.ShowBytes(remainingBytes);
                    }
                    else
                    {
                        DataReader.Comment($"... ({endOfSource - DataReader.BaseStream.Position} bytes of data not shown)");
                    }
                }
                else if (additionalSourceBytes <= SOURCE_BYTES_TO_SHOW && additionalSourceBytes > 0)
                {
                    DataReader.ShowBytes(additionalSourceBytes);
                }
                else
                {
                    DataReader.OutputWriteLine("// no source present");
                }
                DataReader.BaseStream.Position = endOfSource;
                DataReader.BreakLine();
                DataReader.ShowByteCount();
                DataReader.ShowBytes(16, "DXIL(hlsl) Editor ref.");
                DataReader.BreakLine();
            }
        }
        private void ShowDxbcSources(int dxbcSourceCount)
        {
            for (var sourceId = 0; sourceId < dxbcSourceCount; sourceId++)
            {
                var sourceSize = DataReader.ReadInt32AtPosition();
                DataReader.ShowByteCount();
                DataReader.ShowBytes(4, $"Source size, {sourceSize} bytes");
                DataReader.BreakLine();
                var endOfSource = (int)DataReader.BaseStream.Position + sourceSize;
                DataReader.ShowByteCount($"DXBC-SOURCE[{sourceId}]");
                if (sourceSize == 0)
                {
                    DataReader.OutputWriteLine("// no source present");
                }
                if (sourceSize > SOURCE_BYTES_TO_SHOW)
                {
                    DataReader.ShowBytes(SOURCE_BYTES_TO_SHOW, breakLine: false);
                    DataReader.OutputWrite(" ");
                    DataReader.Comment($"... ({endOfSource - DataReader.BaseStream.Position} bytes of data not shown)");
                }
                else if (sourceSize <= SOURCE_BYTES_TO_SHOW && sourceSize > 0)
                {
                    DataReader.ShowBytes(sourceSize);
                }
                DataReader.BaseStream.Position = endOfSource;
                DataReader.BreakLine();
                DataReader.ShowByteCount();
                DataReader.ShowBytes(16, "DXBC(hlsl) Editor ref.");
                DataReader.BreakLine();
            }
        }

        const int VULKAN_SOURCE_BYTES_TO_SHOW = 192;
        private void ShowVulkanSources(int vulkanSourceCount)
        {
            for (var i = 0; i < vulkanSourceCount; i++)
            {
                var offsetToEditorId = DataReader.ReadInt32AtPosition();
                if (offsetToEditorId == 0)
                {
                    DataReader.ShowBytes(4);
                    DataReader.OutputWriteLine("// no source present");
                    DataReader.BreakLine();
                }
                else
                {
                    DataReader.ShowByteCount();
                    DataReader.ShowBytes(4, $"({offsetToEditorId}) offset to Editor ref. ID ");
                    var endOfSourceOffset = (int)DataReader.BaseStream.Position + offsetToEditorId;
                    var arg0 = DataReader.ReadInt32AtPosition();
                    DataReader.ShowBytes(4, $"({arg0}) values seen for Vulkan sources are (2,3)");
                    var offset2 = DataReader.ReadInt32AtPosition();
                    DataReader.ShowBytes(4, $"({offset2}) - looks like an offset, unknown significance");
                    DataReader.BreakLine();
                    DataReader.ShowByteCount($"VULKAN-SOURCE[{i}]");
                    var sourceSize = offsetToEditorId - 8;
                    var bytesToShow = VULKAN_SOURCE_BYTES_TO_SHOW > sourceSize ? sourceSize : VULKAN_SOURCE_BYTES_TO_SHOW;
                    DataReader.ShowBytes(bytesToShow);
                    var bytesNotShown = sourceSize - bytesToShow;
                    if (bytesNotShown > 0)
                    {
                        DataReader.Comment($"... {bytesNotShown} bytes of data not shown)");
                    }
                    DataReader.BreakLine();
                    DataReader.BaseStream.Position = endOfSourceOffset;
                }
                DataReader.ShowBytes(16, "Vulkan Editor ref. ID");
                DataReader.BreakLine();
            }
        }

        private void ShowGlslSources(int glslSourceCount)
        {
            for (var sourceId = 0; sourceId < glslSourceCount; sourceId++)
            {
                var sourceSize = ShowGlslSourceOffsets();
                var sourceOffset = (int)DataReader.BaseStream.Position;
                ShowZGlslSourceSummary(sourceId);
                DataReader.ShowByteCount();
                var fileIdBytes = DataReader.ReadBytes(16);
                var fileIdStr = BytesToString(fileIdBytes);
                DataReader.OutputWrite(fileIdStr);
                DataReader.TabComment($" Editor ref.");
                DataReader.BreakLine();
            }
        }
        public int ShowGlslSourceOffsets()
        {
            DataReader.ShowByteCount("glsl source offsets");
            var offset1 = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytesWithIntValue();
            if (offset1 == 0)
            {
                return 0;
            }
            DataReader.ShowBytes(4, breakLine: false);
            DataReader.TabComment("always 3");
            var sourceSize = DataReader.ReadInt32AtPosition() - 1; // one less because of null-term
            DataReader.ShowBytesWithIntValue();
            DataReader.BreakLine();
            return sourceSize;
        }
        public void ShowZGlslSourceSummary(int sourceId)
        {
            var bytesToRead = DataReader.ReadInt32AtPosition(-4);
            var endOfSource = (int)DataReader.BaseStream.Position + bytesToRead;
            DataReader.ShowByteCount($"GLSL-SOURCE[{sourceId}]");
            if (bytesToRead == 0)
            {
                DataReader.OutputWriteLine("// no source present");
            }
            if (bytesToRead > SOURCE_BYTES_TO_SHOW)
            {
                DataReader.ShowBytes(SOURCE_BYTES_TO_SHOW);
                DataReader.Comment($"... ({endOfSource - DataReader.BaseStream.Position} bytes of data not shown)");
            }
            else if (bytesToRead <= SOURCE_BYTES_TO_SHOW && bytesToRead > 0)
            {
                DataReader.ShowBytes(bytesToRead);
            }
            DataReader.BaseStream.Position = endOfSource;
            DataReader.BreakLine();
        }
        public void ShowZAllEndBlocksTypeVs(bool hullShader = false)
        {
            DataReader.ShowByteCount();
            var nr_end_blocks = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, breakLine: false);
            DataReader.TabComment($"nr end blocks ({nr_end_blocks})");
            DataReader.BreakLine();
            for (var i = 0; i < nr_end_blocks; i++)
            {
                DataReader.ShowBytes(16 + (hullShader ? 1 : 0));
            }
        }

        private void ShowDynamicExpression(int dynExpLen)
        {
            var dynExpDatabytes = DataReader.ReadBytesAtPosition(0, dynExpLen);
            var dynExp = ParseDynamicExpression(dynExpDatabytes);
            DataReader.OutputWriteLine($"// {dynExp}");
            DataReader.ShowBytes(dynExpLen);
        }
    }
}

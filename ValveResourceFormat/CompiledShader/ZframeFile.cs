using System;
using System.IO;
using System.Collections.Generic;
using System.Text;
using ValveResourceFormat.ThirdParty;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;

namespace ValveResourceFormat.CompiledShader
{
    public class ZFrameFile : IDisposable
    {
        public ShaderDataReader datareader { get; private set; }
        public string filenamepath { get; }
        public VcsProgramType vcsProgramType { get; }
        public VcsPlatformType vcsPlatformType { get; }
        public VcsShaderModelType vcsShaderModelType { get; }
        public long zframeId { get; }
        public ZDataBlock leadingData { get; }
        public List<ZFrameParam> zframeParams { get; } = new();
        public int[] leadingSummary { get; } = Array.Empty<int>();
        public List<ZDataBlock> dataBlocks { get; } = new();
        public int[] trailingSummary { get; }
        public byte[] flags0 { get; }
        public int flagbyte0 { get; }
        public int gpuSourceCount { get; }
        public int flagbyte1 { get; }
        public List<GpuSource> gpuSources { get; } = new();
        public List<VsEndBlock> vsEndBlocks { get; } = new();
        public List<PsEndBlock> psEndBlocks { get; } = new();
        public int nrEndBlocks { get; }
        public int nonZeroDataBlockCount { get; }

        public ZFrameFile(byte[] databytes, string filenamepath, long zframeId, VcsProgramType vcsProgramType,
            VcsPlatformType vcsPlatformType, VcsShaderModelType vcsShaderModelType, bool omitParsing = false, HandleOutputWrite outputWriter = null)
        {
            this.filenamepath = filenamepath;
            this.vcsProgramType = vcsProgramType;
            this.vcsPlatformType = vcsPlatformType;
            this.vcsShaderModelType = vcsShaderModelType;
            datareader = new ShaderDataReader(new MemoryStream(databytes), outputWriter);
            this.zframeId = zframeId;

            // in case of failure; enable omitParsing and use the datareader directly
            // the zframe encoding for Features files has not been determined (only found in v62 files)
            if (omitParsing || vcsProgramType == VcsProgramType.Features)
            {
                return;
            }

            leadingData = new ZDataBlock(datareader, -1);
            int paramCount = datareader.ReadInt16();
            for (int i = 0; i < paramCount; i++)
            {
                ZFrameParam zParam = new(datareader);
                zframeParams.Add(zParam);
            }
            // this data is only found in vertex shaders
            if (this.vcsProgramType == VcsProgramType.VertexShader)
            {
                int summaryLength = datareader.ReadInt16();
                leadingSummary = new int[summaryLength];
                for (int i = 0; i < summaryLength; i++)
                {
                    leadingSummary[i] = datareader.ReadInt16();
                }
            }
            int dataBlockCount = datareader.ReadInt16();
            for (int blockId = 0; blockId < dataBlockCount; blockId++)
            {
                ZDataBlock dataBlock = new(datareader, blockId);
                if (dataBlock.h0 > 0)
                {
                    nonZeroDataBlockCount++;
                }
                dataBlocks.Add(dataBlock);
            }
            int tailSummaryLength = datareader.ReadInt16();
            trailingSummary = new int[tailSummaryLength];
            for (int i = 0; i < tailSummaryLength; i++)
            {
                trailingSummary[i] = datareader.ReadInt16();
            }
            flags0 = datareader.ReadBytes(4);
            flagbyte0 = datareader.ReadByte();
            gpuSourceCount = datareader.ReadInt32();
            flagbyte1 = datareader.ReadByte();

            if (vcsPlatformType == VcsPlatformType.PC)
            {
                switch (vcsShaderModelType)
                {
                    case VcsShaderModelType._20:
                    case VcsShaderModelType._2b:
                    case VcsShaderModelType._30:
                    case VcsShaderModelType._31:
                        ReadDxilSources(gpuSourceCount);
                        break;
                    case VcsShaderModelType._40:
                    case VcsShaderModelType._41:
                    case VcsShaderModelType._50:
                    case VcsShaderModelType._60:
                        ReadDxbcSources(gpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported model type {vcsPlatformType} {vcsShaderModelType}");
                }
            } else
            {
                switch (vcsPlatformType)
                {
                    case VcsPlatformType.PCGL:
                    case VcsPlatformType.MOBILE_GLES:
                        ReadGlslSources(gpuSourceCount);
                        break;
                    case VcsPlatformType.VULKAN:
                    case VcsPlatformType.ANDROID_VULKAN:
                    case VcsPlatformType.IOS_VULKAN:
                        ReadVulkanSources(gpuSourceCount);
                        break;
                    default:
                        throw new ShaderParserException($"Unknown or unsupported source type {vcsPlatformType}");
                }
            }
            nrEndBlocks = datareader.ReadInt32();
            for (int i = 0; i < nrEndBlocks; i++)
            {
                if (this.vcsProgramType == VcsProgramType.VertexShader ||
                    this.vcsProgramType == VcsProgramType.GeometryShader || this.vcsProgramType == VcsProgramType.ComputeShader)
                {
                    VsEndBlock vsEndBlock = new(datareader);
                    vsEndBlocks.Add(vsEndBlock);
                } else
                {
                    PsEndBlock psEndBlock = new(datareader);
                    psEndBlocks.Add(psEndBlock);
                }
            }
            if (datareader.BaseStream.Position != datareader.BaseStream.Length)
            {
                throw new ShaderParserException("End of file expected");
            }
        }

        private void ReadGlslSources(int glslSourceCount)
        {
            for (int sourceId = 0; sourceId < glslSourceCount; sourceId++)
            {
                GlslSource glslSource = new(datareader, sourceId);
                gpuSources.Add(glslSource);
            }
        }
        private void ReadDxilSources(int dxilSourceCount)
        {
            for (int sourceId = 0; sourceId < dxilSourceCount; sourceId++)
            {
                DxilSource dxilSource = new(datareader, sourceId);
                gpuSources.Add(dxilSource);
            }
        }
        private void ReadDxbcSources(int dxbcSourceCount)
        {
            for (int sourceId = 0; sourceId < dxbcSourceCount; sourceId++)
            {
                DxbcSource dxbcSource = new(datareader, sourceId);
                gpuSources.Add(dxbcSource);
            }
        }
        private void ReadVulkanSources(int vulkanSourceCount)
        {
            for (int sourceId = 0; sourceId < vulkanSourceCount; sourceId++)
            {
                VulkanSource vulkanSource = new(datareader, sourceId);
                gpuSources.Add(vulkanSource);
            }
        }

        public ZDataBlock GetDataBlock(int blockId)
        {
            return blockId == -1 ? leadingData : dataBlocks[blockId];
        }

        public string ZFrameHeaderStringDescription()
        {
            string zframeHeaderString = "";
            foreach (ZFrameParam zParam in zframeParams)
            {
                zframeHeaderString += $"{zParam}\n";
            }
            return zframeHeaderString;
        }

        public void Dispose()
        {
            Dispose(true);
            GC.SuppressFinalize(this);
        }
        protected virtual void Dispose(bool disposing)
        {
            if (disposing && datareader != null)
            {
                datareader.Dispose();
                datareader = null;
            }
        }

        /*
         * Prints the GPU source as text (GLSL) or bytes (DXIL, DXBC, Vulkan)
         *
         * Method accepts a HandleOutputWrite to redirect output, this needed by the GUI
         * when opening data in different window tabs.
         *
         */
        public void PrintGpuSource(int sourceId, HandleOutputWrite outputWriter = null)
        {
            outputWriter ??= ((x) => { Console.Write(x); });

            if (gpuSources[sourceId] is GlslSource)
            {
                GlslSource glslSource = gpuSources[sourceId] as GlslSource;
                string result = Encoding.UTF8.GetString(glslSource.sourcebytes);
                if (result.Length == 0)
                {
                    outputWriter("[empty source]");
                }
                else
                {
                    outputWriter(result);
                }
            }
            else
            {
                outputWriter($"// {gpuSources[sourceId].GetBlockName()}[{sourceId}] source bytes (" +
                    $"{gpuSources[sourceId].sourcebytes.Length}) ref={gpuSources[sourceId].GetEditorRefIdAsString()}\n");
                outputWriter(BytesToString(gpuSources[sourceId].sourcebytes)+"\n");
            }
        }

        public class ZFrameParam
        {
            public string name0 { get; }
            public uint murmur32 { get; }
            public byte[] code { get; }
            public byte headerOperator { get; }
            public int dynExpLen { get; } = -1;
            public byte[] dynExpression { get; }
            public string dynExpEvaluated { get; }
            public int operatorVal { get; } = int.MinValue;

            public ZFrameParam(ShaderDataReader datareader)
            {
                name0 = datareader.ReadNullTermString();
                murmur32 = datareader.ReadUInt32();
                uint murmurCheck = MurmurHash2.Hash(name0.ToLower(), ShaderFile.PI_MURMURSEED);
                if (murmur32 != murmurCheck)
                {
                    throw new ShaderParserException("not a murmur string!");
                }
                code = datareader.ReadBytes(3);
                headerOperator = code[0];
                if (headerOperator == 0x0e)
                {
                    return;
                }
                dynExpLen = datareader.ReadInt32();
                if (dynExpLen > 0)
                {
                    dynExpression = datareader.ReadBytes(dynExpLen);
                    dynExpEvaluated = ParseDynamicExpression(dynExpression);
                    return;
                }
                if (headerOperator == 1 || headerOperator == 9)
                {
                    operatorVal = datareader.ReadByte();
                    return;
                }
                if (headerOperator == 5)
                {
                    operatorVal = datareader.ReadInt32(); ;
                    return;
                }
                throw new ShaderParserException("unexpected data!");
            }
            public override string ToString()
            {
                if (dynExpLen > 0)
                {
                    return $"{name0,-40} 0x{murmur32:x08}     {BytesToString(code)}   {dynExpEvaluated}";
                } else
                {
                    return $"{name0,-40} 0x{murmur32:x08}     {BytesToString(code)}   {(operatorVal != int.MinValue ? $"{operatorVal}" : "")}";
                }
            }
        }

        public class VsEndBlock
        {
            public byte[] databytes { get; }
            public int blockIdRef { get; }
            public int arg0 { get; }
            public int sourceRef { get; }
            public int sourcePointer { get; }
            public VsEndBlock(ShaderDataReader datareader)
            {
                databytes = datareader.ReadBytesAtPosition(0, 16);
                blockIdRef = datareader.ReadInt32();
                arg0 = datareader.ReadInt32();
                sourceRef = datareader.ReadInt32();
                sourcePointer = datareader.ReadInt32();
            }
        }

        public class PsEndBlock
        {
            public int blockIdRef { get; }
            public int arg0 { get; }
            public int sourceRef { get; }
            public int sourcePointer { get; }
            public bool hasData0 { get; }
            public bool hasData1 { get; }
            public bool hasData2 { get; }
            public byte[] data0 { get; }
            public byte[] data1 { get; }
            public byte[] data2 { get; }
            public PsEndBlock(ShaderDataReader datareader)
            {
                blockIdRef = datareader.ReadInt32();
                arg0 = datareader.ReadInt32();
                sourceRef = datareader.ReadInt32();
                sourcePointer = datareader.ReadInt32();
                int flag0 = datareader.ReadByte();
                int flag1 = datareader.ReadByte();
                int flag2 = datareader.ReadByte();

                if (flag0 != 0 && flag0 != 1 || flag1 != 0 && flag1 != 1 || flag2 != 0 && flag2 != 1)
                {
                    throw new ShaderParserException("unexpected data");
                }
                hasData0 = flag0 == 0;
                hasData1 = flag1 == 0;
                hasData2 = flag2 == 0;

                if (hasData0)
                {
                    data0 = datareader.ReadBytes(16);
                }
                if (hasData1)
                {
                    data1 = datareader.ReadBytes(20);
                }
                if (hasData2)
                {
                    data2 = datareader.ReadBytes(75);
                }
            }
        }

        public void PrintByteAnalysis()
        {
            datareader.BaseStream.Position = 0;
            if (vcsProgramType == VcsProgramType.Features)
            {
                datareader.Comment("Zframe byte data (encoding for features files has not been determined)");
                datareader.ShowBytes((int)datareader.BaseStream.Length);
                return;
            }

            ShowZDataSection(-1);
            ShowZFrameHeader();
            // this applies only to vs files (ps, gs and psrs files don't have this section)
            if (vcsProgramType == VcsProgramType.VertexShader)
            {
                int blockCountInput = datareader.ReadInt16AtPosition();
                datareader.ShowByteCount("Unknown additional parameters, non 'FF FF' entries point to configurations (block IDs)");
                datareader.ShowBytes(2, breakLine: false);
                datareader.TabComment($"nr of data-blocks ({blockCountInput})");
                datareader.ShowBytes(blockCountInput * 2);
                datareader.OutputWriteLine("");
            }
            int blockCount = datareader.ReadInt16AtPosition();
            datareader.ShowByteCount("Data blocks");
            datareader.ShowBytes(2, breakLine: false);
            datareader.TabComment($"nr of data-blocks ({blockCount})");
            datareader.OutputWriteLine("");
            for (int i = 0; i < blockCount; i++)
            {
                ShowZDataSection(i);
            }
            datareader.BreakLine();
            datareader.ShowByteCount("Unknown additional parameters, non 'FF FF' entries point to active block IDs");
            int blockCountOutput = datareader.ReadInt16AtPosition();
            datareader.ShowBytes(2, breakLine: false);
            datareader.TabComment($"nr of data-blocks ({blockCountOutput})", 1);
            datareader.ShowBytes(blockCountOutput * 2);
            datareader.BreakLine();
            datareader.ShowByteCount();
            byte flagbyte = datareader.ReadByteAtPosition();
            datareader.ShowBytes(1, $"possible control byte ({flagbyte}) or flags ({Convert.ToString(flagbyte, 2).PadLeft(8, '0')})");
            datareader.ShowBytes(1, "values seen (0,1,2)");
            datareader.ShowBytes(1, "always 0");
            datareader.ShowBytes(1, "always 0");
            datareader.ShowBytes(1, "values seen (0,1)");
            datareader.BreakLine();
            datareader.ShowByteCount($"Start of source section, {datareader.BaseStream.Position} is " +
                $"the base offset for end-section source pointers");
            int gpuSourceCount = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"{vcsPlatformType} source files ({gpuSourceCount})");
            datareader.ShowBytes(1, "unknown boolean, values seen 0,1", tabLen: 13);
            datareader.BreakLine();

            if (vcsPlatformType == VcsPlatformType.PC)
            {
                switch (vcsShaderModelType)
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
                        throw new ShaderParserException($"Unknown or unsupported model type {vcsPlatformType} {vcsShaderModelType}");
                }
            } else
            {
                switch (vcsPlatformType)
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
                        throw new ShaderParserException($"Unknown or unsupported source type {vcsPlatformType}");
                }
            }

            //  End blocks for vs, gs and cs files
            if (vcsProgramType == VcsProgramType.VertexShader || vcsProgramType == VcsProgramType.GeometryShader || vcsProgramType == VcsProgramType.ComputeShader)
            {
                ShowZAllEndBlocksTypeVs();
                datareader.BreakLine();
            }
            //  End blocks for ps and psrs files
            if (vcsProgramType == VcsProgramType.PixelShader || vcsProgramType == VcsProgramType.PixelShaderRenderState)
            {
                datareader.ShowByteCount();
                int nrEndBlocks = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment($"nr of end blocks ({nrEndBlocks})");
                datareader.OutputWriteLine("");
                for (int i = 0; i < nrEndBlocks; i++)
                {
                    datareader.ShowByteCount($"End-block[{i}]");
                    int blockId = datareader.ReadInt16AtPosition();
                    datareader.ShowBytes(4, breakLine: false);
                    datareader.TabComment($"blockId ref ({blockId})");
                    datareader.ShowBytes(4, breakLine: false);
                    datareader.TabComment("always 0");
                    int sourceReference = datareader.ReadInt16AtPosition();
                    datareader.ShowBytes(4, breakLine: false);
                    datareader.TabComment($"source ref ({sourceReference})");
                    uint glslPointer = datareader.ReadUInt32AtPosition();
                    datareader.ShowBytes(4, breakLine: false);
                    datareader.TabComment($"glsl source pointer ({glslPointer})");
                    bool hasData0 = datareader.ReadByteAtPosition(0) == 0;
                    bool hasData1 = datareader.ReadByteAtPosition(1) == 0;
                    bool hasData2 = datareader.ReadByteAtPosition(2) == 0;
                    datareader.ShowBytes(3, breakLine: false);
                    datareader.TabComment($"(data0={hasData0}, data1={hasData1}, data2={hasData2})", 7);
                    if (hasData0)
                    {
                        datareader.OutputWriteLine("// data-section 0");
                        datareader.ShowBytes(16);
                    }
                    if (hasData1)
                    {
                        datareader.OutputWriteLine("// data-section 1");
                        datareader.ShowBytes(20);
                    }
                    if (hasData2)
                    {
                        datareader.OutputWriteLine("// data-section 2");
                        datareader.ShowBytes(3);
                        datareader.ShowBytes(8);
                        datareader.ShowBytes(64, 32);
                    }
                    datareader.OutputWriteLine("");
                }
            }
            datareader.ShowEndOfFile();
        }

        private bool prevBlockWasZero;
        public void ShowZDataSection(int blockId)
        {
            int blockSize = ShowZBlockDataHeader(blockId);
            ShowZBlockDataBody(blockSize);
        }
        public int ShowZBlockDataHeader(int blockId)
        {
            int arg0 = datareader.ReadInt32AtPosition();
            int arg1 = datareader.ReadInt32AtPosition(4);
            int arg2 = datareader.ReadInt32AtPosition(8);

            if (blockId != -1 && arg0 == 0 && arg1 == 0 && arg2 == 0)
            {
                datareader.ShowBytes(12, breakLine: false);
                datareader.TabComment($"data-block[{blockId}]");
                return 0;
            }
            string comment = "";
            if (blockId == -1)
            {
                comment = $"leading data";
            }
            if (blockId >= 0)
            {
                comment = $"data-block[{blockId}]";
            }
            int blockSize = datareader.ReadInt32AtPosition();
            if (prevBlockWasZero)
            {
                datareader.OutputWriteLine("");
            }
            datareader.ShowByteCount(comment);
            datareader.ShowBytesWithIntValue();
            datareader.ShowBytesWithIntValue();
            datareader.ShowBytesWithIntValue();
            if (blockId == -1 && arg0 == 0 && arg1 == 0 && arg2 == 0)
            {
                datareader.BreakLine();
            }
            return blockSize * 4;
        }
        public void ShowZBlockDataBody(int byteSize)
        {
            if (byteSize == 0)
            {
                prevBlockWasZero = true;
                return;
            } else
            {
                prevBlockWasZero = false;
            }
            datareader.Comment($"{byteSize / 4}*4 bytes");
            datareader.ShowBytes(byteSize);
            datareader.BreakLine();
        }
        public void ShowZFrameHeader()
        {
            datareader.ShowByteCount("Frame header");
            uint nrArgs = datareader.ReadUInt16AtPosition();
            datareader.ShowBytes(2, breakLine: false);
            datareader.TabComment($"nr of arguments ({nrArgs})");
            datareader.OutputWriteLine("");

            for (int i = 0; i < nrArgs; i++)
            {
                ShowMurmurString();
                // int headerOperator = databytes[offset];
                int headerOperator = datareader.ReadByteAtPosition();
                if (headerOperator == 0x0e)
                {
                    datareader.ShowBytes(3);
                    continue;
                }
                if (headerOperator == 1)
                {
                    int dynExpLen = datareader.ReadInt32AtPosition(3);
                    if (dynExpLen == 0)
                    {
                        datareader.ShowBytes(8);
                        continue;
                    } else
                    {
                        datareader.ShowBytes(7);
                        ShowDynamicExpression(dynExpLen);
                        continue;
                    }
                }
                if (headerOperator == 9)
                {
                    int dynExpLen = datareader.ReadInt32AtPosition(3);
                    if (dynExpLen == 0)
                    {
                        datareader.ShowBytes(8);
                        continue;
                    } else
                    {
                        datareader.ShowBytes(7);
                        ShowDynamicExpression(dynExpLen);
                        continue;
                    }
                }
                if (headerOperator == 5)
                {
                    int dynExpLen = datareader.ReadInt32AtPosition(3);
                    if (dynExpLen == 0)
                    {
                        datareader.ShowBytes(11);
                        continue;
                    } else
                    {
                        datareader.ShowBytes(7);
                        ShowDynamicExpression(dynExpLen);
                        continue;
                    }
                }
            }
            if (nrArgs > 0)
            {
                datareader.BreakLine();
            }
        }

        const int SOURCE_BYTES_TO_SHOW = 96;
        private void ShowDxilSources(int dxilSourceCount)
        {
            for (int i = 0; i < dxilSourceCount; i++)
            {
                int sourceOffset = datareader.ReadInt32AtPosition();
                datareader.ShowByteCount();
                datareader.ShowBytes(4, $"offset to end of source {sourceOffset} (taken from {datareader.BaseStream.Position + 4})");
                int additionalSourceBytes = 0;
                if (sourceOffset > 0)
                {
                    datareader.ShowBytes(4);
                    int unknown_prog_uint16 = (int)datareader.ReadUInt16AtPosition(2);
                    datareader.ShowBytes(4, $"({unknown_prog_uint16}) the first ({unknown_prog_uint16} * 4) " +
                        $"bytes look like header data that may need to be processed");
                    datareader.BreakLine();
                    datareader.ShowByteCount($"DXIL-SOURCE[{i}]");
                    int sourceSize = sourceOffset - 8;
                    if (unknown_prog_uint16 > 0)
                    {
                        datareader.ShowBytes(unknown_prog_uint16 * 4);
                    }
                    additionalSourceBytes = sourceSize - unknown_prog_uint16 * 4;
                }
                int endOfSource = (int)datareader.BaseStream.Position + additionalSourceBytes;
                if (additionalSourceBytes > SOURCE_BYTES_TO_SHOW)
                {
                    datareader.ShowBytes(SOURCE_BYTES_TO_SHOW, breakLine: false);
                    datareader.OutputWrite(" ");
                    int remainingBytes = endOfSource - (int)datareader.BaseStream.Position;
                    if (remainingBytes < 50)
                    {
                        datareader.ShowBytes(remainingBytes);
                    } else
                    {
                        datareader.Comment($"... ({endOfSource - datareader.BaseStream.Position} bytes of data not shown)");
                    }
                } else if (additionalSourceBytes <= SOURCE_BYTES_TO_SHOW && additionalSourceBytes > 0)
                {
                    datareader.ShowBytes(additionalSourceBytes);
                } else
                {
                    datareader.OutputWriteLine("// no source present");
                }
                datareader.BaseStream.Position = endOfSource;
                datareader.BreakLine();
                datareader.ShowByteCount();
                datareader.ShowBytes(16, "DXIL(hlsl) Editor ref.");
                datareader.BreakLine();
            }
        }
        private void ShowDxbcSources(int dxbcSourceCount)
        {
            for (int sourceId = 0; sourceId < dxbcSourceCount; sourceId++)
            {
                int sourceSize = datareader.ReadInt32AtPosition();
                datareader.ShowByteCount();
                datareader.ShowBytes(4, $"Source size, {sourceSize} bytes");
                datareader.BreakLine();
                int endOfSource = (int)datareader.BaseStream.Position + sourceSize;
                datareader.ShowByteCount($"DXBC-SOURCE[{sourceId}]");
                if (sourceSize == 0)
                {
                    datareader.OutputWriteLine("// no source present");
                }
                if (sourceSize > SOURCE_BYTES_TO_SHOW)
                {
                    datareader.ShowBytes(SOURCE_BYTES_TO_SHOW, breakLine: false);
                    datareader.OutputWrite(" ");
                    datareader.Comment($"... ({endOfSource - datareader.BaseStream.Position} bytes of data not shown)");
                } else if (sourceSize <= SOURCE_BYTES_TO_SHOW && sourceSize > 0)
                {
                    datareader.ShowBytes(sourceSize);
                }
                datareader.BaseStream.Position = endOfSource;
                datareader.BreakLine();
                datareader.ShowByteCount();
                datareader.ShowBytes(16, "DXBC(hlsl) Editor ref.");
                datareader.BreakLine();
            }
        }

        const int VULKAN_SOURCE_BYTES_TO_SHOW = 192;
        private void ShowVulkanSources(int vulkanSourceCount)
        {
            for (int i = 0; i < vulkanSourceCount; i++)
            {
                int offsetToEditorId = datareader.ReadInt32AtPosition();
                if (offsetToEditorId == 0)
                {
                    datareader.ShowBytes(4);
                    datareader.OutputWriteLine("// no source present");
                    datareader.BreakLine();
                } else
                {
                    datareader.ShowByteCount();
                    datareader.ShowBytes(4, $"({offsetToEditorId}) offset to Editor ref. ID ");
                    int endOfSourceOffset = (int)datareader.BaseStream.Position + offsetToEditorId;
                    int arg0 = datareader.ReadInt32AtPosition();
                    datareader.ShowBytes(4, $"({arg0}) values seen for Vulkan sources are (2,3)");
                    int offset2 = datareader.ReadInt32AtPosition();
                    datareader.ShowBytes(4, $"({offset2}) - looks like an offset, unknown significance");
                    datareader.BreakLine();
                    datareader.ShowByteCount($"VULKAN-SOURCE[{i}]");
                    int sourceSize = offsetToEditorId - 8;
                    int bytesToShow = VULKAN_SOURCE_BYTES_TO_SHOW > sourceSize ? sourceSize : VULKAN_SOURCE_BYTES_TO_SHOW;
                    datareader.ShowBytes(bytesToShow);
                    int bytesNotShown = sourceSize - bytesToShow;
                    if (bytesNotShown > 0)
                    {
                        datareader.Comment($"... {bytesNotShown} bytes of data not shown)");
                    }
                    datareader.BreakLine();
                    datareader.BaseStream.Position = endOfSourceOffset;
                }
                datareader.ShowBytes(16, "Vulkan Editor ref. ID");
                datareader.BreakLine();
            }
        }

        private void ShowGlslSources(int glslSourceCount)
        {
            for (int sourceId = 0; sourceId < glslSourceCount; sourceId++)
            {
                int sourceSize = ShowGlslSourceOffsets();
                int sourceOffset = (int)datareader.BaseStream.Position;
                ShowZGlslSourceSummary(sourceId);
                datareader.ShowByteCount();
                byte[] fileIdBytes = datareader.ReadBytes(16);
                string fileIdStr = BytesToString(fileIdBytes);
                datareader.OutputWrite(fileIdStr);
                datareader.TabComment($" Editor ref.");
                datareader.BreakLine();
            }
        }
        public int ShowGlslSourceOffsets()
        {
            datareader.ShowByteCount("glsl source offsets");
            uint offset1 = datareader.ReadUInt32AtPosition();
            datareader.ShowBytesWithIntValue();
            if (offset1 == 0)
            {
                return 0;
            }
            datareader.ShowBytes(4, breakLine: false);
            datareader.TabComment("always 3");
            int sourceSize = datareader.ReadInt32AtPosition() - 1; // one less because of null-term
            datareader.ShowBytesWithIntValue();
            datareader.BreakLine();
            return sourceSize;
        }
        public void ShowZGlslSourceSummary(int sourceId)
        {
            int bytesToRead = datareader.ReadInt32AtPosition(-4);
            int endOfSource = (int)datareader.BaseStream.Position + bytesToRead;
            datareader.ShowByteCount($"GLSL-SOURCE[{sourceId}]");
            if (bytesToRead == 0)
            {
                datareader.OutputWriteLine("// no source present");
            }
            if (bytesToRead > SOURCE_BYTES_TO_SHOW)
            {
                datareader.ShowBytes(SOURCE_BYTES_TO_SHOW);
                datareader.Comment($"... ({endOfSource - datareader.BaseStream.Position} bytes of data not shown)");
            } else if (bytesToRead <= SOURCE_BYTES_TO_SHOW && bytesToRead > 0)
            {
                datareader.ShowBytes(bytesToRead);
            }
            datareader.BaseStream.Position = endOfSource;
            datareader.BreakLine();
        }
        public void ShowZAllEndBlocksTypeVs()
        {
            datareader.ShowByteCount();
            int nr_end_blocks = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, breakLine: false);
            datareader.TabComment($"nr end blocks ({nr_end_blocks})");
            datareader.BreakLine();
            for (int i = 0; i < nr_end_blocks; i++)
            {
                datareader.ShowBytes(16);
            }
        }
        private void ShowMurmurString()
        {
            string nulltermstr = datareader.ReadNullTermStringAtPosition();
            uint murmur32 = datareader.ReadUInt32AtPosition(nulltermstr.Length + 1);
            uint murmurCheck = MurmurHash2.Hash(nulltermstr.ToLower(), ShaderFile.PI_MURMURSEED);
            if (murmur32 != murmurCheck)
            {
                throw new ShaderParserException("not a murmur string!");
            }
            datareader.Comment($"{nulltermstr} | 0x{murmur32:x08}");
            datareader.ShowBytes(nulltermstr.Length + 1 + 4);
        }
        private void ShowDynamicExpression(int dynExpLen)
        {
            byte[] dynExpDatabytes = datareader.ReadBytesAtPosition(0, dynExpLen);
            string dynExp = ParseDynamicExpression(dynExpDatabytes);
            datareader.OutputWriteLine($"// {dynExp}");
            datareader.ShowBytes(dynExpLen);
        }
    }

}

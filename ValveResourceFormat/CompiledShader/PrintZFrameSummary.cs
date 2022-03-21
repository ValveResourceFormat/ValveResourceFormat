using System;
using System.Collections.Generic;
using System.IO;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using static ValveResourceFormat.CompiledShader.ZFrameFile;

namespace ValveResourceFormat.CompiledShader
{
    public class PrintZFrameSummary
    {
        public HandleOutputWrite OutputWriter { get; set; }
        private ShaderFile shaderFile;
        private ZFrameFile zframeFile;
        private bool showRichTextBoxLinks;

        // If OutputWriter is left as null; output will be written to Console.
        // Otherwise output is directed to the passed HandleOutputWrite object (defined by the calling application, for example GUI element or file)
        public PrintZFrameSummary(ShaderFile shaderFile, ZFrameFile zframeFile,
            HandleOutputWrite OutputWriter = null, bool showRichTextBoxLinks = false)
        {
            this.shaderFile = shaderFile;
            this.zframeFile = zframeFile;
            this.OutputWriter = OutputWriter ?? ((x) => { Console.Write(x); });

            if (zframeFile.vcsProgramType == VcsProgramType.Features)
            {
                OutputWriteLine("Zframe byte data (encoding for features files has not been determined)");
                zframeFile.datareader.BaseStream.Position = 0;
                string zframeBytes = zframeFile.datareader.ReadBytesAsString((int)zframeFile.datareader.BaseStream.Length);
                OutputWriteLine(zframeBytes);
                return;
            }

            this.showRichTextBoxLinks = showRichTextBoxLinks;
            if (showRichTextBoxLinks)
            {
                OutputWriteLine($"Byte printout \\\\{Path.GetFileName(shaderFile.filenamepath)}-ZFRAME{zframeFile.zframeId:x08}-databytes");
                OutputWriteLine("");
            }
            PrintConfigurationState();
            PrintFrameLeadingArgs();
            SortedDictionary<int, int> writeSequences = GetWriteSequences();
            PrintWriteSequences(writeSequences);
            PrintDataBlocks(writeSequences);
            PrintLeadSummary();
            PrintTailSummary();
            PrintSourceSummary();
            PrintEndBlocks();
        }

        private void PrintConfigurationState()
        {
            string configHeader = "Configuration";
            OutputWriteLine(configHeader);
            OutputWriteLine(new string('-', configHeader.Length));
            ConfigMappingSParams configGen = new(shaderFile);
            int[] configState = configGen.GetConfigState(zframeFile.zframeId);
            for (int i = 0; i < configState.Length; i++)
            {
                OutputWriteLine($"{shaderFile.sfBlocks[i].name0,-30} {configState[i]}");
            }
            if (configState.Length == 0)
            {
                OutputWriteLine("[no static params]");
            }
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private void PrintFrameLeadingArgs()
        {
            string headerText = "Frame Header";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            OutputWrite(zframeFile.ZFrameHeaderStringDescription());
            if (zframeFile.zframeParams.Count == 0)
            {
                OutputWriteLine("[empty frameheader]");
            }
            OutputWriteLine("");
            OutputWriteLine("");
        }

        /*
         * rarely if leadingData.h0 is 0 it is given a writeseq of 0 while the writesequence itself will be empty
         * otherwise writeseq = -1 means empty writesequences
         */
        private SortedDictionary<int, int> GetWriteSequences()
        {
            Dictionary<string, int> writeSequences = new();
            SortedDictionary<int, int> sequencesMap = new();
            int seqCount = 0;
            // IMP the first entry is always set 0 regardless of whether the leading datablock carries any data
            sequencesMap.Add(zframeFile.leadingData.blockId, 0);
            if (zframeFile.leadingData.h0 == 0)
            {
                writeSequences.Add("", seqCount++);
            } else
            {
                writeSequences.Add(BytesToString(zframeFile.leadingData.dataload, -1), seqCount++);
            }
            foreach (ZDataBlock zBlock in zframeFile.dataBlocks)
            {
                if (zBlock.dataload == null)
                {
                    sequencesMap.Add(zBlock.blockId, -1);
                    continue;
                }
                string dataloadStr = BytesToString(zBlock.dataload, -1);
                int seq = writeSequences.GetValueOrDefault(dataloadStr, -1);
                if (seq == -1)
                {
                    writeSequences.Add(dataloadStr, seqCount);
                    sequencesMap.Add(zBlock.blockId, seqCount);
                    seqCount++;
                } else
                {
                    sequencesMap.Add(zBlock.blockId, seq);
                }
            }
            return sequencesMap;
        }

        private void PrintWriteSequences(SortedDictionary<int, int> writeSequences)
        {
            string headerText = "Parameter glsl mapping";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));

            int lastseq = writeSequences[-1];
            if (zframeFile.leadingData.h0 > 0)
            {
                OutputWriteLine("");
            }
            string seqName = $"WRITESEQ[{lastseq}] (default)";
            ZDataBlock leadData = zframeFile.leadingData;
            PrintParamWriteSequence(shaderFile, leadData.dataload, leadData.h0, leadData.h1, leadData.h2, seqName: seqName);
            OutputWriteLine("");
            foreach (var item in writeSequences)
            {
                if (item.Value > lastseq)
                {
                    lastseq = item.Value;
                    ZDataBlock zBlock = zframeFile.dataBlocks[item.Key];
                    seqName = $"WRITESEQ[{lastseq}]";
                    PrintParamWriteSequence(shaderFile, zBlock.dataload, zBlock.h0, zBlock.h1, zBlock.h2, seqName: seqName);
                    OutputWriteLine("");
                }
            }
            OutputWriteLine("");
        }

        private void PrintParamWriteSequence(ShaderFile shaderFile, byte[] dataload, int h0, int h1, int h2, string seqName = "")
        {
            if (h0 == 0)
            {
                OutputWriteLine("[empty writesequence]");
                return;
            }
            string b2Desc = "dest";
            string b3Desc = "control";
            // string dataBlockHeader = $"{new string(' ', 5)} {new string(' ', 28)} {b2Desc,-11} {b3Desc}";
            string dataBlockHeader = $"{seqName,-35} {b2Desc,-11} {b3Desc}";
            OutputWriteLine(dataBlockHeader);
            for (int i = 0; i < h0; i++)
            {
                int paramId = dataload[i * 4];
                int b2 = dataload[i * 4 + 2];
                int b3 = dataload[i * 4 + 3];
                string b2Text = $"{b2,3} ({b2:X02})";
                if (b2 == 0xff)
                {
                    b2Text = $"  _ ({b2:X02})";
                }
                string b3Text = $"{b3,3} ({b3:X02})";
                if (b3 == 0xff)
                {
                    b3Text = $"  _ ({b2:X02})";
                }
                OutputWrite($"[{paramId,3}] {shaderFile.paramBlocks[paramId].name0,-30} {b2Text,-14} {b3Text}");
                if (i + 1 == h0 && h0 != h2)
                {
                    OutputWrite($"   // {h0}");
                }
                if (i + 1 == h1)
                {
                    OutputWrite($"   // {h1}");
                }
                if (i + 1 == h2)
                {
                    OutputWrite($"   // {h2}");
                }
                OutputWriteLine("");
            }
        }

        private void PrintDataBlocks(SortedDictionary<int, int> writeSequences)
        {
            Dictionary<int, GpuSource> blockIdToSource = GetBlockIdToSource(zframeFile);
            string configHeader = $"D-Param configurations ({blockIdToSource.Count})";
            OutputWriteLine(configHeader);
            OutputWriteLine(new string('-', configHeader.Length));
            PrintAbbreviations();
            List<int> activeBlockIds = GetActiveBlockIds();
            List<string> dParamNames = new();
            foreach (DBlock dBlock in shaderFile.dBlocks)
            {
                dParamNames.Add(ShortenShaderParam(dBlock.name0).ToLower());
            }
            string configNames = CombineStringsSpaceSep(dParamNames.ToArray(), 6);
            configNames = $"{new string(' ', 5)}{configNames}";
            int dBlockCount = 0;
            foreach (int blockId in activeBlockIds)
            {
                if (dBlockCount % 100 == 0)
                {
                    OutputWriteLine($"{configNames}");
                }
                dBlockCount++;
                int[] dBlockConfig = shaderFile.GetDBlockConfig(blockId);
                string configStr = CombineIntsSpaceSep(dBlockConfig, 6);
                string writeSeqText = $"WRITESEQ[{writeSequences[blockId]}]";
                if (writeSequences[blockId] == -1)
                {
                    writeSeqText = "[empty]";
                }
                OutputWrite($"[{blockId:X02}] {configStr}        {writeSeqText,-12}");
                GpuSource blockSource = blockIdToSource[blockId];

                if (blockSource is GlslSource source)
                {
                    // string sourceLink = $"<a href='{fileTokens.GetGlslHtmlUrl((GlslSource) blockSource)}'>{urlText}</a>";
                    // OutputWriteLine($"    {sourceLink} {blockSource.sourcebytes.Length,12}  (bytes)");

                    if (showRichTextBoxLinks)
                    {
                        OutputWriteLine($"\\\\source\\{blockSource.sourceId}");
                    } else
                    {
                        string sourceDesc = $"source[{blockSource.GetEditorRefIdAsString()}]";
                        OutputWriteLine(sourceDesc);
                    }
                } else
                {
                    OutputWriteLine($"  {blockSource.GetBlockName().PadRight(20)} {blockSource.sourcebytes.Length,12} (bytes)");
                }
            }
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private void PrintAbbreviations()
        {
            List<string> abbreviations = new();
            foreach (var dBlock in shaderFile.dBlocks)
            {
                string abbreviation = $"{dBlock.name0}({ShortenShaderParam(dBlock.name0).ToLower()})";
                abbreviations.Add(abbreviation);
            }
            if (abbreviations.Count == 0)
            {
                return;
            }

            string[] breakabbreviations = CombineValuesBreakString(abbreviations.ToArray(), 120);
            if (breakabbreviations.Length == 1 && breakabbreviations[0].Length == 0)
            {
                return;
            }
            foreach (string abbr in breakabbreviations)
            {
                OutputWriteLine(abbr);
            }
            OutputWriteLine("");
        }

        private List<int> GetActiveBlockIds()
        {
            List<int> blockIds = new();
            if (zframeFile.vcsProgramType == VcsProgramType.VertexShader ||
                zframeFile.vcsProgramType == VcsProgramType.GeometryShader || zframeFile.vcsProgramType == VcsProgramType.ComputeShader)
            {
                foreach (VsEndBlock vsEndBlock in zframeFile.vsEndBlocks)
                {
                    blockIds.Add(vsEndBlock.blockIdRef);
                }
            } else
            {
                foreach (PsEndBlock psEndBlock in zframeFile.psEndBlocks)
                {
                    blockIds.Add(psEndBlock.blockIdRef);
                }
            }
            return blockIds;
        }

        static Dictionary<int, GpuSource> GetBlockIdToSource(ZFrameFile zframeFile)
        {
            Dictionary<int, GpuSource> blockIdToSource = new();
            if (zframeFile.vcsProgramType == VcsProgramType.VertexShader ||
                zframeFile.vcsProgramType == VcsProgramType.GeometryShader || zframeFile.vcsProgramType == VcsProgramType.ComputeShader)
            {
                foreach (VsEndBlock vsEndBlock in zframeFile.vsEndBlocks)
                {
                    blockIdToSource.Add(vsEndBlock.blockIdRef, zframeFile.gpuSources[vsEndBlock.sourceRef]);
                }
            } else
            {
                foreach (PsEndBlock psEndBlock in zframeFile.psEndBlocks)
                {
                    blockIdToSource.Add(psEndBlock.blockIdRef, zframeFile.gpuSources[psEndBlock.sourceRef]);
                }
            }
            return blockIdToSource;
        }

        private void PrintLeadSummary()
        {
            if (zframeFile.vcsProgramType != VcsProgramType.VertexShader)
            {
                return;
            }
            OutputWriteLine(zframeFile.GetLeadSummary());
            OutputWriteLine("");
        }

        private void PrintTailSummary()
        {
            OutputWriteLine(zframeFile.GetTailSummary());
            OutputWriteLine("");
        }

        private void PrintSourceSummary()
        {
            string headerText = "source bytes/flags";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            int b0 = zframeFile.flags0[0];
            int b1 = zframeFile.flags0[1];
            int b2 = zframeFile.flags0[2];
            int b3 = zframeFile.flags0[3];
            OutputWriteLine($"{b0:X02}      // possible control byte ({b0}) or flags ({Convert.ToString(b0, 2).PadLeft(8, '0')})");
            OutputWriteLine($"{b1:X02}      // values seen (0,1,2)");
            OutputWriteLine($"{b2:X02}      // always 0");
            OutputWriteLine($"{b3:X02}      // always 0");
            OutputWriteLine($"{zframeFile.flagbyte0}       // values seen 0,1");
            OutputWriteLine($"{zframeFile.gpuSourceCount,-6}  // nr of source files");
            OutputWriteLine($"{zframeFile.flagbyte1}       // values seen 0,1");
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private static string ByteToBinary(int b0)
        {
            string byteString = "";
            byteString += $"{Convert.ToString(b0 >> 4, 2).PadLeft(4, '0')}";
            byteString += " ";
            byteString += $"{Convert.ToString(b0 & 0xf, 2).PadLeft(4, '0')}";
            return byteString;
        }

        private void PrintEndBlocks()
        {
            string headerText = "End blocks";
            OutputWriteLine($"{headerText}");
            OutputWriteLine(new string('-', headerText.Length));

            VcsProgramType vcsFiletype = shaderFile.vcsProgramType;
            if (vcsFiletype == VcsProgramType.VertexShader || vcsFiletype == VcsProgramType.GeometryShader || vcsFiletype == VcsProgramType.ComputeShader)
            {
                OutputWriteLine($"{zframeFile.vsEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.vsEndBlocks.Count})");
                OutputWriteLine("");
                foreach (VsEndBlock vsEndBlock in zframeFile.vsEndBlocks)
                {
                    OutputWriteLine($"block-ref         {vsEndBlock.blockIdRef}");
                    OutputWriteLine($"arg0              {vsEndBlock.arg0}");
                    OutputWriteLine($"source-ref        {vsEndBlock.sourceRef}");
                    OutputWriteLine($"source-pointer    {vsEndBlock.sourcePointer}");
                    OutputWriteLine($"{BytesToString(vsEndBlock.databytes)}");
                    OutputWriteLine("");
                }
            } else
            {
                OutputWriteLine($"{zframeFile.psEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.psEndBlocks.Count})");
                OutputWriteLine("");
                foreach (PsEndBlock psEndBlock in zframeFile.psEndBlocks)
                {
                    OutputWriteLine($"block-ref         {psEndBlock.blockIdRef}");
                    OutputWriteLine($"arg0              {psEndBlock.arg0}");
                    OutputWriteLine($"source-ref        {psEndBlock.sourceRef}");
                    OutputWriteLine($"source-pointer    {psEndBlock.sourcePointer}");
                    OutputWriteLine($"has data ({psEndBlock.hasData0},{psEndBlock.hasData1},{psEndBlock.hasData2})");
                    if (psEndBlock.hasData0)
                    {
                        OutputWriteLine("// data-section 0");
                        OutputWriteLine($"{BytesToString(psEndBlock.data0)}");
                    }
                    if (psEndBlock.hasData1)
                    {
                        OutputWriteLine("// data-section 1");
                        OutputWriteLine($"{BytesToString(psEndBlock.data1)}");
                    }
                    if (psEndBlock.hasData2)
                    {
                        OutputWriteLine("// data-section 2");
                        OutputWriteLine($"{BytesToString(psEndBlock.data2[0..3])}");
                        // OutputWriteLine($"{BytesToString(psEndBlock.data2[3..11])}");
                        // OutputWriteLine($"{BytesToString(psEndBlock.data2[11..75])}");

                        OutputWriteLine($"{BytesToString(psEndBlock.data2[3..27])}");
                        OutputWriteLine($"{BytesToString(psEndBlock.data2[27..51])}");
                        OutputWriteLine($"{BytesToString(psEndBlock.data2[51..75])}");
                    }
                    OutputWriteLine("");
                }
            }
        }

        public void OutputWrite(string text)
        {
            OutputWriter(text);
        }

        public void OutputWriteLine(string text)
        {
            OutputWrite(text + "\n");
        }
    }
}

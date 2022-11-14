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
        private readonly ShaderFile shaderFile;
        private readonly ZFrameFile zframeFile;
        private readonly bool showRichTextBoxLinks;

        // If OutputWriter is left as null; output will be written to Console.
        // Otherwise output is directed to the passed HandleOutputWrite object (defined by the calling application, for example GUI element or file)
        public PrintZFrameSummary(ShaderFile shaderFile, ZFrameFile zframeFile,
            HandleOutputWrite outputWriter = null, bool showRichTextBoxLinks = false)
        {
            this.shaderFile = shaderFile;
            this.zframeFile = zframeFile;
            this.OutputWriter = outputWriter ?? ((x) => { Console.Write(x); });

            if (zframeFile.vcsProgramType == VcsProgramType.Features)
            {
                OutputWriteLine("Zframe byte data (encoding for features files has not been determined)");
                zframeFile.datareader.BaseStream.Position = 0;
                var zframeBytes = zframeFile.datareader.ReadBytesAsString((int)zframeFile.datareader.BaseStream.Length);
                OutputWriteLine(zframeBytes);
                return;
            }

            this.showRichTextBoxLinks = showRichTextBoxLinks;
            if (showRichTextBoxLinks)
            {
                OutputWriteLine($"View byte detail \\\\{Path.GetFileName(shaderFile.filenamepath)}-ZFRAME{zframeFile.zframeId:x08}-databytes");
                OutputWriteLine("");
            }
            PrintConfigurationState();
            PrintFrameLeadingArgs();
            var writeSequences = GetWriteSequences();
            PrintWriteSequences(writeSequences);
            PrintDataBlocks(writeSequences);

            if (zframeFile.vcsProgramType == VcsProgramType.VertexShader)
            {
                OutputWriteLine($"// configuration states ({zframeFile.leadingSummary.Length}), leading summary\n");
                OutputWriteLine(SummarizeBytes(zframeFile.leadingSummary) + "\n");
            }
            OutputWriteLine($"// configuration states ({zframeFile.trailingSummary.Length}), trailing summary\n");
            OutputWriteLine(SummarizeBytes(zframeFile.trailingSummary) + "\n");
            OutputWrite("\n");

            PrintSourceSummary();
            PrintEndBlocks();
        }

        private void PrintConfigurationState()
        {
            var configHeader = "Configuration";
            OutputWriteLine(configHeader);
            OutputWriteLine(new string('-', configHeader.Length));
            OutputWriteLine("The static configuration this zframe belongs to (zero or more static parameters)\n");
            ConfigMappingSParams configGen = new(shaderFile);
            var configState = configGen.GetConfigState(zframeFile.zframeId);
            for (var i = 0; i < configState.Length; i++)
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
            var headerText = "ZFrame Headers";
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
         * Occasionally leadingData.h0 (leadingData is the first datablock, always present) is 0 we create the empty
         * write sequence WRITESEQ[0] (configurations may refer to it) otherwise sequences assigned -1 mean the write
         * sequence doesn't contain any data and not needed.
         *
         */
        private SortedDictionary<int, int> GetWriteSequences()
        {
            Dictionary<string, int> writeSequences = new();
            SortedDictionary<int, int> sequencesMap = new();
            var seqCount = 0;
            // IMP the first entry is always set 0 regardless of whether the leading datablock carries any data
            sequencesMap.Add(zframeFile.leadingData.blockId, 0);
            if (zframeFile.leadingData.h0 == 0)
            {
                writeSequences.Add("", seqCount++);
            }
            else
            {
                writeSequences.Add(BytesToString(zframeFile.leadingData.dataload, -1), seqCount++);
            }

            foreach (var zBlock in zframeFile.dataBlocks)
            {
                if (zBlock.dataload == null)
                {
                    sequencesMap.Add(zBlock.blockId, -1);
                    continue;
                }
                var dataloadStr = BytesToString(zBlock.dataload, -1);
                var seq = writeSequences.GetValueOrDefault(dataloadStr, -1);
                if (seq == -1)
                {
                    writeSequences.Add(dataloadStr, seqCount);
                    sequencesMap.Add(zBlock.blockId, seqCount);
                    seqCount++;
                }
                else
                {
                    sequencesMap.Add(zBlock.blockId, seq);
                }
            }
            return sequencesMap;
        }

        private void PrintWriteSequences(SortedDictionary<int, int> writeSequences)
        {
            var headerText = "Parameter write sequences";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            OutputWriteLine(
                "This data (thought to be buffer write sequences) appear to be linked to the dynamic (D-param) configurations;\n" +
                "each configuration points to exactly one sequence. WRITESEQ[0] is always defined and considered 'default'.\n");

            var lastseq = writeSequences[-1];
            if (zframeFile.leadingData.h0 > 0)
            {
                OutputWriteLine("");
            }
            var seqName = $"WRITESEQ[{lastseq}] (default)";
            var leadData = zframeFile.leadingData;
            PrintParamWriteSequence(shaderFile, leadData.dataload, leadData.h0, leadData.h1, leadData.h2, seqName: seqName);
            OutputWriteLine("");
            foreach (var item in writeSequences)
            {
                if (item.Value > lastseq)
                {
                    lastseq = item.Value;
                    var zBlock = zframeFile.dataBlocks[item.Key];
                    seqName = $"WRITESEQ[{lastseq}]";
                    PrintParamWriteSequence(shaderFile, zBlock.dataload, zBlock.h0, zBlock.h1, zBlock.h2, seqName: seqName);
                    OutputWriteLine("");
                }
            }
            OutputWriteLine("");
        }

        private void PrintParamWriteSequence(ShaderFile shaderFile, byte[] dataload, int h0, int h1, int h2, string seqName = "")
        {
            var b2Desc = "dest";
            var b3Desc = "control";
            var dataBlockHeader = $"{seqName,-35} {b2Desc,-11} {b3Desc}";
            OutputWriteLine(dataBlockHeader);
            if (h0 == 0)
            {
                OutputWriteLine("[empty writesequence]");
                return;
            }
            for (var i = 0; i < h0; i++)
            {
                int paramId = dataload[i * 4];
                int b2 = dataload[i * 4 + 2];
                int b3 = dataload[i * 4 + 3];
                var b2Text = $"{b2,3} ({b2:X02})";
                if (b2 == 0xff)
                {
                    b2Text = $"  _ ({b2:X02})";
                }
                var b3Text = $"{b3,3} ({b3:X02})";
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
            var blockIdToSource = GetBlockIdToSource(zframeFile);
            var configHeader = $"Dynamic (D-Param) configurations ({blockIdToSource.Count} defined)";
            OutputWriteLine(configHeader);
            OutputWriteLine(new string('-', configHeader.Length));
            OutputWriteLine(
                "Each dynamic parameters has 1 or more defined states. The disabled state (0) is shown as '_'\n" +
                "All permitted configurations are listed with their matching write sequence and GPU source (there is exactly\n" +
                "one of these for each configuration). To save space, the parameter names (original names starting with D_)\n" +
                "are shortened to 3-5 length strings (shown in parenthesis).\n");
            PrintAbbreviations();
            var activeBlockIds = GetActiveBlockIds();
            List<string> dParamNames = new();
            foreach (var dBlock in shaderFile.dBlocks)
            {
                dParamNames.Add(ShortenShaderParam(dBlock.name0).ToLower());
            }
            var configNames = CombineStringsSpaceSep(dParamNames.ToArray(), 6);
            configNames = $"{new string(' ', 5)}{configNames}";
            var dBlockCount = 0;
            foreach (var blockId in activeBlockIds)
            {
                if (dBlockCount % 100 == 0)
                {
                    OutputWriteLine($"{configNames}");
                }
                dBlockCount++;
                var dBlockConfig = shaderFile.GetDBlockConfig(blockId);
                var configStr = CombineIntsSpaceSep(dBlockConfig, 6);
                var writeSeqText = $"WRITESEQ[{writeSequences[blockId]}]";
                if (writeSequences[blockId] == -1)
                {
                    writeSeqText = "[empty]";
                }
                OutputWrite($"[{blockId:X02}] {configStr}   {writeSeqText,-12}");
                var blockSource = blockIdToSource[blockId];

                if (showRichTextBoxLinks)
                {
                    OutputWriteLine($"{blockSource.GetBlockName()}[{blockSource.sourceId}] \\\\source\\{blockSource.sourceId}");
                }
                else
                {
                    var sourceDesc = $"{blockSource.GetBlockName()}[{blockSource.GetEditorRefIdAsString()}]";
                    OutputWriteLine(sourceDesc);
                }
            }
            OutputWriteLine("\n");
        }

        private void PrintAbbreviations()
        {
            List<string> abbreviations = new();
            foreach (var dBlock in shaderFile.dBlocks)
            {
                var abbreviation = $"{dBlock.name0}({ShortenShaderParam(dBlock.name0).ToLower()})";
                abbreviations.Add(abbreviation);
            }
            if (abbreviations.Count == 0)
            {
                return;
            }

            var breakabbreviations = CombineValuesBreakString(abbreviations.ToArray(), 120);
            if (breakabbreviations.Length == 1 && breakabbreviations[0].Length == 0)
            {
                return;
            }
            foreach (var abbr in breakabbreviations)
            {
                OutputWriteLine(abbr);
            }
            OutputWriteLine("");
        }

        private List<int> GetActiveBlockIds()
        {
            List<int> blockIds = new();
            if (zframeFile.vcsProgramType == VcsProgramType.VertexShader || zframeFile.vcsProgramType == VcsProgramType.GeometryShader ||
                zframeFile.vcsProgramType == VcsProgramType.ComputeShader || zframeFile.vcsProgramType == VcsProgramType.DomainShader ||
                zframeFile.vcsProgramType == VcsProgramType.HullShader)
            {
                foreach (var vsEndBlock in zframeFile.vsEndBlocks)
                {
                    blockIds.Add(vsEndBlock.blockIdRef);
                }
            }
            else
            {
                foreach (var psEndBlock in zframeFile.psEndBlocks)
                {
                    blockIds.Add(psEndBlock.blockIdRef);
                }
            }
            return blockIds;
        }

        static Dictionary<int, GpuSource> GetBlockIdToSource(ZFrameFile zframeFile)
        {
            Dictionary<int, GpuSource> blockIdToSource = new();
            if (zframeFile.vcsProgramType == VcsProgramType.VertexShader || zframeFile.vcsProgramType == VcsProgramType.GeometryShader ||
                zframeFile.vcsProgramType == VcsProgramType.ComputeShader || zframeFile.vcsProgramType == VcsProgramType.DomainShader ||
                zframeFile.vcsProgramType == VcsProgramType.HullShader)
            {
                foreach (var vsEndBlock in zframeFile.vsEndBlocks)
                {
                    blockIdToSource.Add(vsEndBlock.blockIdRef, zframeFile.gpuSources[vsEndBlock.sourceRef]);
                }
            }
            else
            {
                foreach (var psEndBlock in zframeFile.psEndBlocks)
                {
                    blockIdToSource.Add(psEndBlock.blockIdRef, zframeFile.gpuSources[psEndBlock.sourceRef]);
                }
            }
            return blockIdToSource;
        }


        public static string SummarizeBytes(int[] bytes)
        {
            var summaryDesc = "";
            for (var i = 0; i < bytes.Length; i++)
            {
                if (i > 0 && i % 16 == 0)
                {
                    summaryDesc += "\n";
                }
                summaryDesc += bytes[i] > -1 ? $"{bytes[i],-8}" : "_  ".PadRight(8);
            }
            return summaryDesc.Trim();
        }


        private void PrintSourceSummary()
        {
            var headerText = "source bytes/flags";
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
            OutputWriteLine($"{zframeFile.flagbyte1}       // added with v66");
            OutputWriteLine($"{zframeFile.gpuSourceCount,-6}  // nr of source files");
            OutputWriteLine($"{zframeFile.flagbyte2}       // values seen 0,1");
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private static string ByteToBinary(int b0)
        {
            var byteString = "";
            byteString += $"{Convert.ToString(b0 >> 4, 2).PadLeft(4, '0')}";
            byteString += " ";
            byteString += $"{Convert.ToString(b0 & 0xf, 2).PadLeft(4, '0')}";
            return byteString;
        }

        private void PrintEndBlocks()
        {
            var headerText = "End blocks";
            OutputWriteLine($"{headerText}");
            OutputWriteLine(new string('-', headerText.Length));

            var vcsFiletype = shaderFile.vcsProgramType;
            if (vcsFiletype == VcsProgramType.VertexShader || vcsFiletype == VcsProgramType.GeometryShader ||
                vcsFiletype == VcsProgramType.ComputeShader || vcsFiletype == VcsProgramType.DomainShader ||
                vcsFiletype == VcsProgramType.HullShader)
            {
                OutputWriteLine($"{zframeFile.vsEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.vsEndBlocks.Count})");
                OutputWriteLine("");
                foreach (var vsEndBlock in zframeFile.vsEndBlocks)
                {
                    OutputWriteLine($"block-ref         {vsEndBlock.blockIdRef}");
                    OutputWriteLine($"arg0              {vsEndBlock.arg0}");
                    OutputWriteLine($"source-ref        {vsEndBlock.sourceRef}");
                    OutputWriteLine($"source-pointer    {vsEndBlock.sourcePointer}");
                    if (vcsFiletype == VcsProgramType.HullShader)
                    {
                        OutputWriteLine($"hs-arg            {vsEndBlock.hullShaderArg}");
                    }
                    OutputWriteLine($"{BytesToString(vsEndBlock.databytes)}");
                    OutputWriteLine("");
                }
            }
            else
            {
                OutputWriteLine($"{zframeFile.psEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.psEndBlocks.Count})");
                OutputWriteLine("");
                foreach (var psEndBlock in zframeFile.psEndBlocks)
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

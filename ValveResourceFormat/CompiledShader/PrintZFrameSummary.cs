using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

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
            OutputWriter = outputWriter ?? ((x) => { Console.Write(x); });

            if (zframeFile.VcsProgramType == VcsProgramType.Features)
            {
                OutputWriteLine("Zframe byte data (encoding for features files has not been determined)");
                zframeFile.DataReader.BaseStream.Position = 0;
                var zframeBytes = zframeFile.DataReader.ReadBytesAsString((int)zframeFile.DataReader.BaseStream.Length);
                OutputWriteLine(zframeBytes);
                return;
            }

            this.showRichTextBoxLinks = showRichTextBoxLinks;
            if (showRichTextBoxLinks)
            {
                OutputWriteLine($"View byte detail \\\\{Path.GetFileName(shaderFile.FilenamePath)}-ZFRAME{zframeFile.ZframeId:x08}-databytes");
                OutputWriteLine("");
            }
            PrintConfigurationState();
            PrintAttributes();
            var writeSequences = GetBlockToUniqueSequenceMap();
            PrintWriteSequences(writeSequences);
            PrintDynamicConfigurations(writeSequences);
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
            var configState = configGen.GetConfigState(zframeFile.ZframeId);
            for (var i = 0; i < configState.Length; i++)
            {
                OutputWriteLine($"{shaderFile.SfBlocks[i].Name,-30} {configState[i]}");
            }
            if (configState.Length == 0)
            {
                OutputWriteLine("[no static params]");
            }
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private void PrintAttributes()
        {
            var headerText = "Attributes";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            OutputWrite(zframeFile.AttributesStringDescription());
            if (zframeFile.Attributes.Count == 0)
            {
                OutputWriteLine("[no attributes]");
            }
            OutputWriteLine("");
            OutputWriteLine("");
        }

        /*
         * Because the write sequences are often repeated, we only print the unique ones.
         */
        public Dictionary<string, int> GetUniqueWriteSequences()
        {
            Dictionary<string, int> writeSequences = new();
            var seqCount = 0;
            if (zframeFile.LeadingData.H0 == 0)
            {
                writeSequences.Add("", seqCount++);
            }
            else
            {
                writeSequences.Add(BytesToString(zframeFile.LeadingData.Dataload, -1), seqCount++);
            }

            foreach (var zBlock in zframeFile.DataBlocks)
            {
                if (zBlock.Dataload == null)
                {
                    continue;
                }

                var dataloadStr = BytesToString(zBlock.Dataload, -1);
                if (!writeSequences.ContainsKey(dataloadStr))
                {
                    writeSequences.Add(dataloadStr, seqCount++);
                }
            }

            return writeSequences;
        }

        /*
         * Occasionally leadingData.h0 (leadingData is the first datablock, always present) is 0 we create the empty
         * write sequence WRITESEQ[0] (configurations may refer to it) otherwise sequences assigned -1 mean the write
         * sequence doesn't contain any data and not needed.
         */
        public SortedDictionary<int, int> GetBlockToUniqueSequenceMap()
        {
            SortedDictionary<int, int> sequencesMap = new()
            {
                // IMP the first entry is always set 0 regardless of whether the leading datablock carries any data
                { zframeFile.LeadingData.BlockId, 0 }
            };

            var uniqueSequences = GetUniqueWriteSequences();

            foreach (var zBlock in zframeFile.DataBlocks)
            {
                if (zBlock.Dataload == null)
                {
                    sequencesMap.Add(zBlock.BlockId, -1);
                    continue;
                }

                var dataloadStr = BytesToString(zBlock.Dataload, -1);
                sequencesMap.Add(zBlock.BlockId, uniqueSequences[dataloadStr]);
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
                "each configuration points to exactly one sequence. WRITESEQ[0] is always defined.");

            OutputFormatterTabulatedData tabulatedData = new(OutputWriter);
            tabulatedData.DefineHeaders(zframeFile.LeadingData.H0 > 0 ?
                new string[] { "segment", "", "dest", "control" } :
                new string[] { "", "", "", "" });
            if (zframeFile.LeadingData.H0 > 0)
            {
                tabulatedData.AddTabulatedRow(new string[] { "", "", "", "" });
            }
            tabulatedData.AddTabulatedRow(new string[] { "WRITESEQ[0]", "", "", "" });
            var dataBlock0 = zframeFile.LeadingData;
            PrintParamWriteSequence(dataBlock0.Dataload, dataBlock0.H0, dataBlock0.H1, dataBlock0.H2, tabulatedData);
            tabulatedData.AddTabulatedRow(new string[] { "", "", "", "" });

            var lastSeq = writeSequences[-1];
            foreach (var item in writeSequences)
            {
                if (item.Value > lastSeq)
                {
                    lastSeq = item.Value;
                    var dataBlock = zframeFile.DataBlocks[item.Key];
                    tabulatedData.AddTabulatedRow(new string[] { $"WRITESEQ[{lastSeq}]", "", "", "" });
                    PrintParamWriteSequence(dataBlock.Dataload, dataBlock.H0, dataBlock.H1, dataBlock.H2, tabulatedData);
                    tabulatedData.AddTabulatedRow(new string[] { "", "", "", "" });
                }
            }
            tabulatedData.PrintTabulatedValues(spacing: 2);
            OutputWriteLine("");
        }

        private void PrintParamWriteSequence(byte[] dataload, int h0, int h1, int h2,
            OutputFormatterTabulatedData tabulatedData)
        {
            PrintParamWriteSequenceSegment(dataload, segStart: 0, segEnd: h1, segId: 0, tabulatedData);
            PrintParamWriteSequenceSegment(dataload, segStart: h1, segEnd: h2, segId: 1, tabulatedData);
            PrintParamWriteSequenceSegment(dataload, segStart: h2, segEnd: h0, segId: 2, tabulatedData);
        }

        private void PrintParamWriteSequenceSegment(byte[] dataload, int segStart, int segEnd, int segId,
            OutputFormatterTabulatedData tabulatedData)
        {
            if (segEnd > segStart)
            {
                for (var i = segStart; i < segEnd; i++)
                {
                    // What is [i*4+1] - does this sometimes fail?
                    // var paramId = dataload[i * 4] + 256 * dataload[i * 4 + 1];
                    var paramId = dataload[i * 4];
                    var arg0 = dataload[i * 4 + 2];
                    var arg1 = dataload[i * 4 + 3];
                    var segmentDesc = i == segStart ? $"seg_{segId}" : "";
                    var paramDesc = $"{shaderFile.ParamBlocks[paramId].Name}[{paramId}]";
                    var arg0Desc = arg0 == 0xff ? $"{"_",4}" : $"{arg0,4}";
                    var arg1Desc = arg1 == 0xff ? $"{"_",7}" : $"{arg1,7}";
                    tabulatedData.AddTabulatedRow(new string[] { segmentDesc, paramDesc, arg0Desc, arg1Desc });
                }
            }
            else
            {
                tabulatedData.AddTabulatedRow(new string[] { $"seg_{segId}", "[empty]", "", "" });
            }
        }

        private void PrintDynamicConfigurations(SortedDictionary<int, int> writeSequences)
        {
            var blockIdToSource = GetBlockIdToSource(zframeFile);
            var abbreviations = DConfigsAbbreviations();
            var hasOnlyDefaultConfiguration = blockIdToSource.Count == 1;
            var hasNoDConfigsDefined = abbreviations.Count == 0;
            var isVertexShader = zframeFile.VcsProgramType == VcsProgramType.VertexShader;

            var configsDefined = hasOnlyDefaultConfiguration ? "" : $" ({blockIdToSource.Count} defined)";
            var configHeader = $"Dynamic (D-Param) configurations{configsDefined}";
            OutputWriteLine(configHeader);
            OutputWriteLine(new string('-', configHeader.Length));

            OutputFormatterTabulatedData tabulatedConfigNames = new(OutputWriter);
            tabulatedConfigNames.DefineHeaders(new string[] { "", "abbrev." });

            List<string> shortenedNames = new();
            foreach (var abbrev in abbreviations)
            {
                tabulatedConfigNames.AddTabulatedRow(new string[] { $"{abbrev.Item1}", $"{abbrev.Item2}" });
                shortenedNames.Add(abbrev.Item2);
            }

            OutputFormatterTabulatedData tabulatedConfigCombinations = new(OutputWriter);
            tabulatedConfigCombinations.DefineHeaders(shortenedNames.ToArray());

            var activeBlockIds = GetActiveBlockIds();
            foreach (var blockId in activeBlockIds)
            {
                var dBlockConfig = shaderFile.GetDBlockConfig(blockId);
                tabulatedConfigCombinations.AddTabulatedRow(IntArrayToStrings(dBlockConfig, nulledValue: 0));
            }
            var tabbedConfigs = new Stack<string>(tabulatedConfigCombinations.BuildTabulatedRows(reverse: true));
            if (tabbedConfigs.Count == 0)
            {
                OutputWriteLine("No dynamic parameters defined");
            }
            else
            {
                tabulatedConfigNames.PrintTabulatedValues();
            }
            OutputWriteLine("");
            var dNamesHeader = hasNoDConfigsDefined ? "" : tabbedConfigs.Pop();
            var gpuSourceName = zframeFile.GpuSources[0].GetBlockName().ToLower();
            var sourceHeader = $"{gpuSourceName}-source";
            string[] dConfigHeaders = isVertexShader ?
                    new string[] { "config-id", dNamesHeader, "write-seq.", sourceHeader, "gpu-inputs", "unknown-arg" } :
                    new string[] { "config-id", dNamesHeader, "write-seq.", sourceHeader, "unknown-arg" };
            OutputFormatterTabulatedData tabulatedConfigFull = new(OutputWriter);
            tabulatedConfigFull.DefineHeaders(dConfigHeaders);

            var dBlockCount = 0;
            foreach (var blockId in activeBlockIds)
            {
                dBlockCount++;
                if (dBlockCount % 100 == 0)
                {
                    tabulatedConfigFull.AddTabulatedRow(isVertexShader ?
                        new string[] { "", dNamesHeader, "", "", "", "" } :
                        new string[] { "", dNamesHeader, "", "", "" });
                }
                var configIdText = $"0x{blockId:x}";
                var configCombText = hasNoDConfigsDefined ? $"{"(default)",-14}" : tabbedConfigs.Pop();
                var writeSeqText = writeSequences[blockId] == -1 ? "[empty]" : $"seq[{writeSequences[blockId]}]";
                var blockSource = blockIdToSource[blockId];
                var sourceLink = showRichTextBoxLinks ?
                    @$"\\source\{blockSource.SourceId}" :
                    $"{gpuSourceName}[{blockSource.GetEditorRefIdAsString()}]";
                var vsInputs = isVertexShader ?
                    zframeFile.VShaderInputs[blockId] : -1;
                var gpuInputText = vsInputs >= 0 ? $"VS-symbols[{zframeFile.VShaderInputs[blockId]}]" : "[none]";
                var arg0Text = $"{zframeFile.UnknownArg[blockId]}";
                tabulatedConfigFull.AddTabulatedRow(
                    isVertexShader ?
                    new string[] { configIdText, configCombText, writeSeqText, sourceLink, gpuInputText, arg0Text } :
                    new string[] { configIdText, configCombText, writeSeqText, sourceLink, arg0Text });
            }

            tabulatedConfigFull.PrintTabulatedValues();
            if (!hasNoDConfigsDefined)
            {
                OutputWriteLine("");
            }
        }

        private List<(string, string)> DConfigsAbbreviations()
        {
            List<(string, string)> abbreviations = new();
            foreach (var dBlock in shaderFile.DBlocks)
            {
                var abbreviation = ShortenShaderParam(dBlock.Name).ToLowerInvariant();
                abbreviations.Add((dBlock.Name, abbreviation));
            }
            return abbreviations;
        }

        private List<int> GetActiveBlockIds()
        {
            List<int> blockIds = new();
            if (zframeFile.VcsProgramType == VcsProgramType.VertexShader || zframeFile.VcsProgramType == VcsProgramType.GeometryShader ||
                zframeFile.VcsProgramType == VcsProgramType.ComputeShader || zframeFile.VcsProgramType == VcsProgramType.DomainShader ||
                zframeFile.VcsProgramType == VcsProgramType.HullShader)
            {
                foreach (var vsEndBlock in zframeFile.VsEndBlocks)
                {
                    blockIds.Add(vsEndBlock.BlockIdRef);
                }
            }
            else
            {
                foreach (var psEndBlock in zframeFile.PsEndBlocks)
                {
                    blockIds.Add(psEndBlock.BlockIdRef);
                }
            }
            return blockIds;
        }

        static Dictionary<int, GpuSource> GetBlockIdToSource(ZFrameFile zframeFile)
        {
            Dictionary<int, GpuSource> blockIdToSource = new();
            if (zframeFile.VcsProgramType == VcsProgramType.VertexShader || zframeFile.VcsProgramType == VcsProgramType.GeometryShader ||
                zframeFile.VcsProgramType == VcsProgramType.ComputeShader || zframeFile.VcsProgramType == VcsProgramType.DomainShader ||
                zframeFile.VcsProgramType == VcsProgramType.HullShader)
            {
                foreach (var vsEndBlock in zframeFile.VsEndBlocks)
                {
                    blockIdToSource.Add(vsEndBlock.BlockIdRef, zframeFile.GpuSources[vsEndBlock.SourceRef]);
                }
            }
            else
            {
                foreach (var psEndBlock in zframeFile.PsEndBlocks)
                {
                    blockIdToSource.Add(psEndBlock.BlockIdRef, zframeFile.GpuSources[psEndBlock.SourceRef]);
                }
            }
            return blockIdToSource;
        }

        private void PrintSourceSummary()
        {
            var headerText = "source bytes/flags";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            int b0 = zframeFile.Flags0[0];
            int b1 = zframeFile.Flags0[1];
            int b2 = zframeFile.Flags0[2];
            int b3 = zframeFile.Flags0[3];
            OutputWriteLine($"{b0:X02}      // possible control byte ({b0}) or flags ({Convert.ToString(b0, 2).PadLeft(8, '0')})");
            OutputWriteLine($"{b1:X02}      // values seen (0,1,2)");
            OutputWriteLine($"{b2:X02}      // always 0");
            OutputWriteLine($"{b3:X02}      // always 0");
            OutputWriteLine($"{zframeFile.Flagbyte0}       // values seen 0,1");
            OutputWriteLine($"{zframeFile.Flagbyte1}       // added with v66");
            OutputWriteLine($"{zframeFile.GpuSourceCount,-6}  // nr of source files");
            OutputWriteLine($"{zframeFile.Flagbyte2}       // values seen 0,1");
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private void PrintEndBlocks()
        {
            var headerText = "End blocks";
            OutputWriteLine($"{headerText}");
            OutputWriteLine(new string('-', headerText.Length));

            var vcsFiletype = shaderFile.VcsProgramType;
            if (vcsFiletype == VcsProgramType.VertexShader || vcsFiletype == VcsProgramType.GeometryShader ||
                vcsFiletype == VcsProgramType.ComputeShader || vcsFiletype == VcsProgramType.DomainShader ||
                vcsFiletype == VcsProgramType.HullShader)
            {
                OutputWriteLine($"{zframeFile.VsEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.VsEndBlocks.Count})");
                OutputWriteLine("");
                foreach (var vsEndBlock in zframeFile.VsEndBlocks)
                {
                    OutputWriteLine($"block-ref         {vsEndBlock.BlockIdRef}");
                    OutputWriteLine($"arg0              {vsEndBlock.Arg0}");
                    OutputWriteLine($"source-ref        {vsEndBlock.SourceRef}");
                    OutputWriteLine($"source-pointer    {vsEndBlock.SourcePointer}");
                    if (vcsFiletype == VcsProgramType.HullShader)
                    {
                        OutputWriteLine($"hs-arg            {vsEndBlock.HullShaderArg}");
                    }
                    OutputWriteLine($"{BytesToString(vsEndBlock.Databytes)}");
                    OutputWriteLine("");
                }
            }
            else
            {
                OutputWriteLine($"{zframeFile.PsEndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.PsEndBlocks.Count})");
                OutputWriteLine("");
                foreach (var psEndBlock in zframeFile.PsEndBlocks)
                {
                    OutputWriteLine($"block-ref         {psEndBlock.BlockIdRef}");
                    OutputWriteLine($"arg0              {psEndBlock.Arg0}");
                    OutputWriteLine($"source-ref        {psEndBlock.SourceRef}");
                    OutputWriteLine($"source-pointer    {psEndBlock.SourcePointer}");
                    OutputWriteLine($"has data ({psEndBlock.HasData0},{psEndBlock.HasData1},{psEndBlock.HasData2})");
                    if (psEndBlock.HasData0)
                    {
                        OutputWriteLine("// data-section 0");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data0)}");
                    }
                    if (psEndBlock.HasData1)
                    {
                        OutputWriteLine("// data-section 1");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data1)}");
                    }
                    if (psEndBlock.HasData2)
                    {
                        OutputWriteLine("// data-section 2");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data2[0..3])}");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data2[3..27])}");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data2[27..51])}");
                        OutputWriteLine($"{BytesToString(psEndBlock.Data2[51..75])}");
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

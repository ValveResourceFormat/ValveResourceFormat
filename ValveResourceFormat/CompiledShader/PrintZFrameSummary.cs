using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class PrintZFrameSummary
    {
        public HandleOutputWrite OutputWriter { get; set; }
        private readonly ShaderFile shaderFile;
        private readonly VfxStaticComboData zframeFile;
        private readonly bool showRichTextBoxLinks;

        // If OutputWriter is left as null; output will be written to Console.
        // Otherwise output is directed to the passed HandleOutputWrite object (defined by the calling application, for example GUI element or file)
        public PrintZFrameSummary(ShaderFile shaderFile, VfxStaticComboData zframeFile,
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
                OutputWriteLine($"{shaderFile.StaticCombos[i].Name,-30} {configState[i]}");
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
            Dictionary<string, int> writeSequences = [];
            var seqCount = 0;
            writeSequences.Add(BytesToString(zframeFile.LeadingData.Dataload, -1), seqCount++);
            foreach (var zBlock in zframeFile.DataBlocks)
            {
                if (zBlock.Fields.Length == 0)
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
         * Occasionally leadingData field count (leadingData is the first datablock, always present) is 0 we create the empty
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
                if (zBlock.Fields.Length == 0)
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
            var emptyRow = new string[] { "", "", "", "", "" };
            tabulatedData.DefineHeaders(zframeFile.LeadingData.FieldsCount > 0 ?
                ["segment", "", nameof(VfxVariableIndexData.Dest), nameof(VfxVariableIndexData.Control), "flags"] :
                emptyRow);
            if (zframeFile.LeadingData.FieldsCount > 0)
            {
                tabulatedData.AddTabulatedRow(emptyRow);
            }
            tabulatedData.AddTabulatedRow(["WRITESEQ[0]", "", "", "", ""]);
            var dataBlock0 = zframeFile.LeadingData;
            PrintParamWriteSequence(dataBlock0, tabulatedData);
            tabulatedData.AddTabulatedRow(emptyRow);

            var lastSeq = writeSequences[-1];
            foreach (var item in writeSequences)
            {
                if (item.Value > lastSeq)
                {
                    lastSeq = item.Value;
                    var dataBlock = zframeFile.DataBlocks[item.Key];
                    tabulatedData.AddTabulatedRow([$"WRITESEQ[{lastSeq}]", "", "", "", ""]);
                    PrintParamWriteSequence(dataBlock, tabulatedData);
                    tabulatedData.AddTabulatedRow(emptyRow);
                }
            }
            tabulatedData.PrintTabulatedValues(spacing: 2);
            OutputWriteLine("");
        }

        private void PrintParamWriteSequence(VfxVariableIndexArray dataBlock, OutputFormatterTabulatedData tabulatedData)
        {
            PrintParamWriteSequenceSegment(dataBlock.Evaluated, 0, tabulatedData);
            PrintParamWriteSequenceSegment(dataBlock.Segment1, 1, tabulatedData);
            PrintParamWriteSequenceSegment(dataBlock.Globals, 2, tabulatedData);
        }

        private void PrintParamWriteSequenceSegment(IReadOnlyList<VfxVariableIndexData> segment, int segId, OutputFormatterTabulatedData tabulatedData)
        {
            var segmentDesc = segId switch
            {
                0 => "Evaluated",
                2 => "_Globals_",
                _ => "seg_" + segId
            };

            if (segment.Count > 0)
            {
                for (var i = 0; i < segment.Count; i++)
                {
                    var field = segment[i];
                    var paramDesc = $"[{field.VariableIndex}] {shaderFile.VariableDescriptions[field.VariableIndex].Name}";
                    var paramFlags = field.LayoutSet == 0 ? $"{"_",7}" : $"{field.LayoutSet,7}";
                    var arg1Desc = field.Dest == 0xff ? $"{"_",7}" : $"{field.Dest,7}";
                    var arg2Desc = field.Control == 0xff ? $"{"_",10}" : $"{field.Control,10}";
                    tabulatedData.AddTabulatedRow([i == 0 ? segmentDesc : string.Empty, paramDesc, arg1Desc, $"{arg2Desc} ({field.Field2})", paramFlags]);
                }
            }
            else
            {
                tabulatedData.AddTabulatedRow([segmentDesc, "[empty]", "", "", ""]);
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
            tabulatedConfigNames.DefineHeaders(["", "abbrev."]);

            List<string> shortenedNames = [];
            foreach (var abbrev in abbreviations)
            {
                tabulatedConfigNames.AddTabulatedRow([$"{abbrev.Item1}", $"{abbrev.Item2}"]);
                shortenedNames.Add(abbrev.Item2);
            }

            OutputFormatterTabulatedData tabulatedConfigCombinations = new(OutputWriter);
            tabulatedConfigCombinations.DefineHeaders([.. shortenedNames]);

            var activeBlockIds = zframeFile.EndBlocks.Select(endBlock => endBlock.BlockIdRef).ToList();
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
            var gpuSourceName = zframeFile.GpuSources[0].BlockName.ToLowerInvariant();
            var sourceHeader = $"{gpuSourceName}-source";
            string[] dConfigHeaders = isVertexShader ?
                    ["config-id", dNamesHeader, "write-seq.", sourceHeader, "gpu-inputs", nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(GpuSource.HashMD5)] :
                    ["config-id", dNamesHeader, "write-seq.", sourceHeader, nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(GpuSource.HashMD5)];
            OutputFormatterTabulatedData tabulatedConfigFull = new(OutputWriter);
            tabulatedConfigFull.DefineHeaders(dConfigHeaders);

            var dBlockCount = 0;
            foreach (var blockId in activeBlockIds)
            {
                dBlockCount++;
                if (dBlockCount % 100 == 0)
                {
                    tabulatedConfigFull.AddTabulatedRow(isVertexShader ?
                        ["", dNamesHeader, "", "", "", "", "", ""] :
                        ["", dNamesHeader, "", "", "", "", ""]);
                }
                var configIdText = $"0x{blockId:x}";
                var configCombText = hasNoDConfigsDefined ? $"{"(default)",-14}" : tabbedConfigs.Pop();
                var writeSeqText = writeSequences[blockId] == -1 ? "[empty]" : $"seq[{writeSequences[blockId]}]";
                var blockSource = blockIdToSource[blockId];
                var sourceLink = showRichTextBoxLinks ?
                    @$"\\source\{blockSource.SourceId}" :
                    $"{gpuSourceName}[{blockSource.HashMD5}]";
                var vsInputs = isVertexShader ?
                    zframeFile.VShaderInputs[blockId] : -1;
                var gpuInputText = vsInputs >= 0 ? $"VS-symbols[{zframeFile.VShaderInputs[blockId]}]" : "[none]";
                var arg1Text = $"{zframeFile.ConstantBufferBindInfoSlots[blockId]}";
                var arg2Text = $"{zframeFile.ConstantBufferBindInfoFlags[blockId]}";
                var hash = blockSource.HashMD5.ToString();
                tabulatedConfigFull.AddTabulatedRow(
                    isVertexShader ?
                    [configIdText, configCombText, writeSeqText, sourceLink, gpuInputText, arg1Text, arg2Text, hash] :
                    [configIdText, configCombText, writeSeqText, sourceLink, arg1Text, arg2Text, hash]);
            }

            tabulatedConfigFull.PrintTabulatedValues();
            if (!hasNoDConfigsDefined)
            {
                OutputWriteLine("");
            }
        }

        private List<(string, string)> DConfigsAbbreviations()
        {
            List<(string, string)> abbreviations = [];
            foreach (var dBlock in shaderFile.DynamicCombos)
            {
                var abbreviation = ShortenShaderParam(dBlock.Name).ToLowerInvariant();
                abbreviations.Add((dBlock.Name, abbreviation));
            }
            return abbreviations;
        }

        static Dictionary<int, GpuSource> GetBlockIdToSource(VfxStaticComboData zframeFile)
        {
            Dictionary<int, GpuSource> blockIdToSource = [];
            foreach (var endBlock in zframeFile.EndBlocks)
            {
                blockIdToSource.Add(endBlock.BlockIdRef, zframeFile.GpuSources[endBlock.SourceRef]);
            }
            return blockIdToSource;
        }

        private void PrintSourceSummary()
        {
            var headerText = "source bytes/flags";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));
            OutputWriteLine($"{zframeFile.Flags0}      // size?");
            OutputWriteLine($"{zframeFile.Flagbyte0}       //");
            OutputWriteLine($"{zframeFile.Flagbyte1}       // added with v66");
            OutputWriteLine($"{zframeFile.GpuSourceCount,-6}  // nr of source files");
            OutputWriteLine($"{zframeFile.Flagbyte2}       //");
            OutputWriteLine("");
            OutputWriteLine("");
        }

        private void PrintEndBlocks()
        {
            var headerText = $"End blocks";
            OutputWriteLine(headerText);
            OutputWriteLine(new string('-', headerText.Length));

            var vcsFiletype = shaderFile.VcsProgramType;

            OutputWriteLine($"{zframeFile.EndBlocks.Count:X02} 00 00 00   // end blocks ({zframeFile.EndBlocks.Count})");
            OutputWriteLine("");

            foreach (var endBlock in zframeFile.EndBlocks)
            {
                OutputWriteLine($"block-ref         {endBlock.BlockIdRef}");
                OutputWriteLine($"arg0              {endBlock.Arg0}");
                OutputWriteLine($"source-ref        {endBlock.SourceRef}");
                OutputWriteLine($"source-pointer    {endBlock.SourcePointer}");

                if (endBlock is VfxStaticComboData.HsEndBlock hsEndBlock)
                {
                    OutputWriteLine($"hs-arg            {hsEndBlock.HullShaderArg}");
                }
                else if (endBlock is VfxStaticComboData.VfxRenderStateInfo psEndBlock)
                {
                    OutputWriteLine($"has data ({psEndBlock.HasRasterizerState},{psEndBlock.HasStencilState},{psEndBlock.HasBlendState})");
                    if (psEndBlock.HasRasterizerState)
                    {
                        OutputWriteLine("// data-section 0");
                        OutputWriteLine($"{BytesToString(psEndBlock.RsRasterizerStateDesc)}");
                    }
                    if (psEndBlock.HasStencilState)
                    {
                        OutputWriteLine("// data-section 1");
                        OutputWriteLine($"{BytesToString(psEndBlock.RsDepthStencilStateDesc)}");
                    }
                    if (psEndBlock.HasBlendState)
                    {
                        OutputWriteLine("// data-section 2");
                        var data2 = psEndBlock.RsBlendStateDesc.AsSpan();
                        OutputWriteLine($"{BytesToString(data2[0..3])}");
                        OutputWriteLine($"{BytesToString(data2[3..27])}");
                        OutputWriteLine($"{BytesToString(data2[27..51])}");
                        OutputWriteLine($"{BytesToString(data2[51..75])}");
                    }
                }

                OutputWriteLine("");
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

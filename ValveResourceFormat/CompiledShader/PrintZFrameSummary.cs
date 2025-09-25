using System.Diagnostics;
using System.IO;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class PrintZFrameSummary
    {
        public IndentedTextWriter OutputWriter { get; set; }
        private readonly VfxStaticComboData StaticCombo;

        // If OutputWriter is left as null; output will be written to Console.
        // Otherwise output is directed to the passed HandleOutputWrite object (defined by the calling application, for example GUI element or file)
        public PrintZFrameSummary(VfxStaticComboData staticCombo, IndentedTextWriter outputWriter)
        {
            StaticCombo = staticCombo;
            OutputWriter = outputWriter;

            if (staticCombo.ParentProgramData?.VcsProgramType == VcsProgramType.Features)
            {
                return;
            }

            PrintConfigurationState();
            PrintAttributes();
            var writeSequences = GetBlockToUniqueSequenceMap();
            PrintWriteSequences(writeSequences);
            PrintDynamicConfigurations(writeSequences);
            OutputWriter.WriteLine();
            PrintSourceSummary();
            PrintEndBlocks();
        }

        private void PrintConfigurationState()
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            var configHeader = "PARENT STATIC COMBO CONFIGURATION";
            OutputWriter.WriteLine(configHeader);
            ConfigMappingParams configGen = new(StaticCombo.ParentProgramData);
            var configState = configGen.GetConfigState(StaticCombo.StaticComboId);
            for (var i = 0; i < configState.Length; i++)
            {
                OutputWriter.WriteLine($"{StaticCombo.ParentProgramData.StaticComboArray[i].Name,-30} {configState[i]}");
            }
            if (configState.Length == 0)
            {
                OutputWriter.WriteLine("[no static params]");
            }
            OutputWriter.WriteLine();
            OutputWriter.WriteLine();
        }

        private void PrintAttributes()
        {
            OutputWriter.WriteLine("ATTRIBUTES");
            OutputWriter.Write(StaticCombo.AttributesStringDescription());
            if (StaticCombo.Attributes.Length == 0)
            {
                OutputWriter.WriteLine("[no attributes]");
            }
            OutputWriter.WriteLine();
            OutputWriter.WriteLine();
        }

        /*
         * Because the write sequences are often repeated, we only print the unique ones.
         */
        public Dictionary<string, int> GetUniqueWriteSequences()
        {
            Dictionary<string, int> writeSequences = [];
            var seqCount = 0;
            writeSequences.Add(BytesToString(StaticCombo.VariablesFromStaticCombo.Dataload, -1), seqCount++);
            foreach (var zBlock in StaticCombo.DynamicComboVariables)
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
                { StaticCombo.VariablesFromStaticCombo.BlockId, 0 }
            };

            var uniqueSequences = GetUniqueWriteSequences();

            foreach (var zBlock in StaticCombo.DynamicComboVariables)
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
            OutputWriter.WriteLine("DYNAMIC COMBO VARIABLES");

            OutputFormatterTabulatedData tabulatedData = new(OutputWriter);
            var emptyRow = new string[] { "", "", "", "", "" };
            tabulatedData.DefineHeaders(StaticCombo.VariablesFromStaticCombo.Fields.Length > 0 ?
                ["segment", "", nameof(VfxVariableIndexData.Dest), nameof(VfxVariableIndexData.Control), nameof(VfxVariableIndexData.LayoutSet)] :
                emptyRow);
            if (StaticCombo.VariablesFromStaticCombo.Fields.Length > 0)
            {
                tabulatedData.AddTabulatedRow(emptyRow);
            }
            tabulatedData.AddTabulatedRow(["STATIC-SEQ", "", "", "", ""]);
            var dataBlock0 = StaticCombo.VariablesFromStaticCombo;
            PrintParamWriteSequence(dataBlock0, tabulatedData);
            tabulatedData.AddTabulatedRow(emptyRow);

            var lastSeq = writeSequences[-1];
            foreach (var item in writeSequences)
            {
                if (item.Value > lastSeq)
                {
                    lastSeq = item.Value;
                    var dataBlock = StaticCombo.DynamicComboVariables[item.Key];
                    tabulatedData.AddTabulatedRow([$"WRITESEQ[{lastSeq}]", "", "", "", ""]);
                    PrintParamWriteSequence(dataBlock, tabulatedData);
                    tabulatedData.AddTabulatedRow(emptyRow);
                }
            }
            tabulatedData.PrintTabulatedValues(spacing: 2);
            OutputWriter.WriteLine();
        }

        private void PrintParamWriteSequence(VfxVariableIndexArray dataBlock, OutputFormatterTabulatedData tabulatedData)
        {
            PrintParamWriteSequenceSegment(dataBlock.Evaluated, 0, tabulatedData);
            PrintParamWriteSequenceSegment(dataBlock.RenderState, 1, tabulatedData);
            PrintParamWriteSequenceSegment(dataBlock.Globals, 2, tabulatedData);
        }

        private void PrintParamWriteSequenceSegment(IReadOnlyList<VfxVariableIndexData> segment, int segId, OutputFormatterTabulatedData tabulatedData)
        {
            if (segment.Count == 0)
            {
                return;
            }

            var segmentDesc = segId switch
            {
                0 => "Evaluated",
                1 => "RenderState",
                2 => "Constants",
                _ => throw new InvalidDataException(),
            };

            Debug.Assert(StaticCombo.ParentProgramData != null);

            for (var i = 0; i < segment.Count; i++)
            {
                var field = segment[i];
                var paramDesc = $"[{field.VariableIndex}] {StaticCombo.ParentProgramData.VariableDescriptions[field.VariableIndex].Name}";
                var destDesc = field.Dest == 0xff ? $"{"_",7}" : $"{field.Dest,7}";
                var controlDesc = field.Control == 0xff ? $"{"_",10}" : $"{field.Control,10}";
                tabulatedData.AddTabulatedRow([i == 0 ? segmentDesc : string.Empty, paramDesc, destDesc, $"{controlDesc} ({field.Field2})", $"{field.LayoutSet,7}"]);
            }
        }

        private void PrintDynamicConfigurations(SortedDictionary<int, int> writeSequences)
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            var blockIdToSource = GetBlockIdToSource(StaticCombo);
            var abbreviations = DConfigsAbbreviations();
            var hasOnlyDefaultConfiguration = blockIdToSource.Count == 1;
            var hasNoDConfigsDefined = abbreviations.Count == 0;
            var isVertexShader = StaticCombo.ParentProgramData.VcsProgramType == VcsProgramType.VertexShader;

            var configsDefined = hasOnlyDefaultConfiguration ? "" : $" ({blockIdToSource.Count} defined)";
            var configHeader = $"DYNAMIC COMBOS{configsDefined}";
            OutputWriter.WriteLine(configHeader);

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

            foreach (var block in StaticCombo.DynamicCombos)
            {
                var dBlockConfig = StaticCombo.ParentProgramData.GetDBlockConfig(block.DynamicComboId);
                tabulatedConfigCombinations.AddTabulatedRow(IntArrayToStrings(dBlockConfig, nulledValue: 0));
            }
            var tabbedConfigs = new Stack<string>(tabulatedConfigCombinations.BuildTabulatedRows(reverse: true));
            if (tabbedConfigs.Count == 0)
            {
                OutputWriter.WriteLine("[none defined]");
            }
            else
            {
                tabulatedConfigNames.PrintTabulatedValues();
            }
            OutputWriter.WriteLine();
            var dNamesHeader = hasNoDConfigsDefined ? "" : tabbedConfigs.Pop();
            var gpuSourceName = StaticCombo.ShaderFiles.Length > 0
                ? StaticCombo.ShaderFiles[0].BlockName.ToLowerInvariant()
                : "unknown";
            var sourceHeader = $"{gpuSourceName}-source";
            string[] dConfigHeaders = isVertexShader ?
                    ["config-id", dNamesHeader, "write-seq.", sourceHeader, "gpu-inputs", nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(VfxShaderFile.HashMD5)] :
                    ["config-id", dNamesHeader, "write-seq.", sourceHeader, nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(VfxShaderFile.HashMD5)];
            OutputFormatterTabulatedData tabulatedConfigFull = new(OutputWriter);
            tabulatedConfigFull.DefineHeaders(dConfigHeaders);

            var dBlockCount = 0;
            foreach (var block in StaticCombo.DynamicCombos)
            {
                var blockId = (int)block.DynamicComboId;
                dBlockCount++;
                if (dBlockCount % 100 == 0)
                {
                    tabulatedConfigFull.AddTabulatedRow(isVertexShader ?
                        ["", dNamesHeader, "", "", "", "", "", ""] :
                        ["", dNamesHeader, "", "", "", "", ""]);
                }
                var configIdText = $"0x{blockId:X2}";
                var configCombText = hasNoDConfigsDefined ? $"{"(default)",-14}" : tabbedConfigs.Pop();
                var writeSeqText = writeSequences[blockId] == -1 ? "[empty]" : $"SEQ[{writeSequences[blockId]}]";
                var blockSource = blockIdToSource.GetValueOrDefault(blockId);
                if (blockSource is null)
                {
                    return;
                }

                var sourceLink = $"{blockSource.ShaderFileId:X2}";
                var vsInputs = isVertexShader ? StaticCombo.VShaderInputs[block.ShaderFileId] : -1;
                var gpuInputText = vsInputs >= 0 ? $"VS[{vsInputs}]" : "[none]";
                var arg1Text = $"{StaticCombo.ConstantBufferBindInfoSlots[blockId]}";
                var arg2Text = $"{StaticCombo.ConstantBufferBindInfoFlags[blockId]}";
                var hash = blockSource.HashMD5.ToString();
                tabulatedConfigFull.AddTabulatedRow(
                    isVertexShader ?
                    [configIdText, configCombText, writeSeqText, sourceLink, gpuInputText, arg1Text, arg2Text, hash] :
                    [configIdText, configCombText, writeSeqText, sourceLink, arg1Text, arg2Text, hash]);
            }

            tabulatedConfigFull.PrintTabulatedValues();
            if (!hasNoDConfigsDefined)
            {
                OutputWriter.WriteLine();
            }
        }

        private List<(string, string)> DConfigsAbbreviations()
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            List<(string, string)> abbreviations = [];
            foreach (var dBlock in StaticCombo.ParentProgramData.DynamicComboArray)
            {
                var abbreviation = ShortenShaderParam(dBlock.Name).ToLowerInvariant();
                abbreviations.Add((dBlock.Name, abbreviation));
            }
            return abbreviations;
        }

        static Dictionary<long, VfxShaderFile> GetBlockIdToSource(VfxStaticComboData zframeFile)
        {
            Dictionary<long, VfxShaderFile> blockIdToSource = [];
            foreach (var endBlock in zframeFile.DynamicCombos)
            {
                if (endBlock.ShaderFileId != -1)
                {
                    blockIdToSource.Add(endBlock.DynamicComboId, zframeFile.ShaderFiles[endBlock.ShaderFileId]);
                }
            }
            return blockIdToSource;
        }

        private void PrintSourceSummary()
        {
            OutputWriter.WriteLine("source bytes/flags");
            OutputWriter.WriteLine($"{StaticCombo.ConstantBufferSize}      // Constant Buffer Size");
            OutputWriter.WriteLine($"{StaticCombo.Flagbyte0}       //");
            OutputWriter.WriteLine($"{StaticCombo.Flagbyte1}       // added with v66");
            OutputWriter.WriteLine($"{StaticCombo.Flagbyte2}       //");
            OutputWriter.WriteLine();
            OutputWriter.WriteLine();
        }

        private void PrintEndBlocks()
        {
            OutputWriter.WriteLine("RENDER STATE INFO");
            OutputWriter.WriteLine();
            foreach (var endBlock in StaticCombo.DynamicCombos)
            {
                OutputWriter.WriteLine($"block-ref         {endBlock.DynamicComboId}");
                OutputWriter.WriteLine($"source-ref        {endBlock.ShaderFileId}");
                OutputWriter.WriteLine($"source-pointer    {endBlock.SourcePointer}");
                if (endBlock is VfxRenderStateInfoHullShader hsEndBlock)
                {
                    OutputWriter.WriteLine($"hs-arg            {hsEndBlock.HullShaderArg}");
                }
                else if (endBlock is VfxRenderStateInfoPixelShader psEndBlock)
                {
                    if (psEndBlock.RasterizerStateDesc != null)
                    {
                        OutputWriter.WriteLine("// Rasterizer State");
                        var rs = psEndBlock.RasterizerStateDesc;
                        OutputWriter.WriteLine($"{nameof(rs.FillMode)}: {rs.FillMode}, {nameof(rs.CullMode)}: {rs.CullMode}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthClipEnable)}: {rs.DepthClipEnable}, {nameof(rs.MultisampleEnable)}: {rs.MultisampleEnable}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthBias)}: {rs.DepthBias}, {nameof(rs.DepthBiasClamp)}: {rs.DepthBiasClamp}, {nameof(rs.SlopeScaledDepthBias)}: {rs.SlopeScaledDepthBias}");
                    }
                    if (psEndBlock.DepthStencilStateDesc != null)
                    {
                        OutputWriter.WriteLine("// Depth Stencil State");
                        var ds = psEndBlock.DepthStencilStateDesc;
                        OutputWriter.WriteLine($"{nameof(ds.DepthTestEnable)}: {ds.DepthTestEnable}, {nameof(ds.DepthWriteEnable)}: {ds.DepthWriteEnable}, {nameof(ds.DepthFunc)}: {ds.DepthFunc}, {nameof(ds.HiZEnable360)}: {ds.HiZEnable360}, {nameof(ds.HiZWriteEnable360)}: {ds.HiZWriteEnable360}");
                        OutputWriter.WriteLine($"{nameof(ds.StencilEnable)}: {ds.StencilEnable}, {nameof(ds.StencilReadMask)}: {ds.StencilReadMask}, {nameof(ds.StencilWriteMask)}: {ds.StencilWriteMask}, {nameof(ds.FrontStencilFailOp)}: {ds.FrontStencilFailOp}, {nameof(ds.FrontStencilDepthFailOp)}: {ds.FrontStencilDepthFailOp}");
                        OutputWriter.WriteLine($"{nameof(ds.FrontStencilPassOp)}: {ds.FrontStencilPassOp}, {nameof(ds.FrontStencilFunc)}: {ds.FrontStencilFunc}, {nameof(ds.BackStencilFailOp)}: {ds.BackStencilFailOp}, {nameof(ds.BackStencilDepthFailOp)}: {ds.BackStencilDepthFailOp}, {nameof(ds.BackStencilPassOp)}: {ds.BackStencilPassOp}");
                        OutputWriter.WriteLine($"{nameof(ds.BackStencilFunc)}: {ds.BackStencilFunc}, {nameof(ds.HiStencilEnable360)}: {ds.HiStencilEnable360}, {nameof(ds.HiStencilWriteEnable360)}: {ds.HiStencilWriteEnable360}, {nameof(ds.HiStencilFunc360)}: {ds.HiStencilFunc360}, {nameof(ds.HiStencilRef360)}: {ds.HiStencilRef360}");
                    }
                    if (psEndBlock.BlendStateDesc != null)
                    {
                        OutputWriter.WriteLine("// Blend State");
                        var bs = psEndBlock.BlendStateDesc;
                        OutputWriter.WriteLine($"{nameof(bs.AlphaToCoverageEnable)}: {bs.AlphaToCoverageEnable}, {nameof(bs.IndependentBlendEnable)}: {bs.IndependentBlendEnable}, {nameof(bs.HighPrecisionBlendEnable360)}: {bs.HighPrecisionBlendEnable360}");
                        for (var i = 0; i < 8; i++)
                        {
                            OutputWriter.WriteLine($"RT{i}: Enabled={bs.BlendEnable[i]}, SRGB={bs.SrgbWriteEnable[i]}, WriteMask={bs.RenderTargetWriteMask[i]}");
                            OutputWriter.WriteLine($"  RGB: Src={bs.SrcBlend[i]}, Dst={bs.DestBlend[i]}, Op={bs.BlendOp[i]}");
                            OutputWriter.WriteLine($"  Alpha: Src={bs.SrcBlendAlpha[i]}, Dst={bs.DestBlendAlpha[i]}, Op={bs.BlendOpAlpha[i]}");
                        }
                    }
                }
                OutputWriter.WriteLine();
            }
        }
    }
}

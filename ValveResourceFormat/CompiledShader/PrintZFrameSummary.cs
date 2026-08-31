using System.Diagnostics;
using System.IO;
using System.Runtime.InteropServices;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Prints a summary of shader static combo data.
    /// </summary>
    public class PrintZFrameSummary
    {
        /// <summary>Gets or sets the output writer.</summary>
        public IndentedTextWriter OutputWriter { get; set; }
        private readonly VfxStaticComboData StaticCombo;

        /// <summary>
        /// Initializes a new instance and prints the summary.
        /// </summary>
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
            var (uniqueSequences, blockToSequence) = GetWriteSequences();
            PrintWriteSequences(uniqueSequences);
            PrintDynamicConfigurations(blockToSequence);
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
         * The leading datablock (always present) is sequence 0 even when it carries no data,
         * as configurations may refer to it. Blocks without data map to -1.
         */
        /// <summary>
        /// Deduplicates write sequences, returning the unique ones in order of first appearance
        /// along with a map of block IDs to sequence IDs (-1 for blocks without data).
        /// </summary>
        public (List<VfxVariableIndexArray> Unique, SortedDictionary<int, int> BlockToSequence) GetWriteSequences()
        {
            List<VfxVariableIndexArray> unique = [StaticCombo.VariablesFromStaticCombo];
            Dictionary<VfxVariableIndexData[], int> sequenceIds = new(WriteSequenceComparer)
            {
                { StaticCombo.VariablesFromStaticCombo.Fields, 0 }
            };
            SortedDictionary<int, int> blockToSequence = new()
            {
                { StaticCombo.VariablesFromStaticCombo.BlockId, 0 }
            };

            foreach (var zBlock in StaticCombo.DynamicComboVariables)
            {
                if (zBlock.Fields.Length == 0)
                {
                    blockToSequence.Add(zBlock.BlockId, -1);
                    continue;
                }

                if (!sequenceIds.TryGetValue(zBlock.Fields, out var id))
                {
                    id = unique.Count;
                    sequenceIds.Add(zBlock.Fields, id);
                    unique.Add(zBlock);
                }

                blockToSequence.Add(zBlock.BlockId, id);
            }

            return (unique, blockToSequence);
        }

        private static readonly EqualityComparer<VfxVariableIndexData[]> WriteSequenceComparer = EqualityComparer<VfxVariableIndexData[]>.Create(
            static (a, b) => MemoryMarshal.AsBytes(a.AsSpan()).SequenceEqual(MemoryMarshal.AsBytes(b.AsSpan())),
            static a =>
            {
                var hash = new HashCode();
                hash.AddBytes(MemoryMarshal.AsBytes(a.AsSpan()));
                return hash.ToHashCode();
            });

        private void PrintWriteSequences(List<VfxVariableIndexArray> uniqueSequences)
        {
            OutputWriter.WriteLine("DYNAMIC COMBO VARIABLES");

            OutputFormatterTabulatedData tabulatedData = new(OutputWriter);
            var emptyRow = new string[] { "", "", "", "", "" };
            tabulatedData.DefineHeaders(StaticCombo.VariablesFromStaticCombo.Fields.Length > 0
                ? ["segment", "", nameof(VfxVariableIndexData.Dest), nameof(VfxVariableIndexData.Control), nameof(VfxVariableIndexData.LayoutSet)]
                : emptyRow);
            if (StaticCombo.VariablesFromStaticCombo.Fields.Length > 0)
            {
                tabulatedData.AddTabulatedRow(emptyRow);
            }
            tabulatedData.AddTabulatedRow(["STATIC-SEQ", "", "", "", ""]);
            PrintParamWriteSequence(uniqueSequences[0], tabulatedData);
            tabulatedData.AddTabulatedRow(emptyRow);

            for (var seq = 1; seq < uniqueSequences.Count; seq++)
            {
                tabulatedData.AddTabulatedRow([$"WRITESEQ[{seq}]", "", "", "", ""]);
                PrintParamWriteSequence(uniqueSequences[seq], tabulatedData);
                tabulatedData.AddTabulatedRow(emptyRow);
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
            string[] dConfigHeaders = isVertexShader
                    ? ["config-id", dNamesHeader, "write-seq.", sourceHeader, "gpu-inputs", nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(VfxShaderFile.HashMD5)]
                    : ["config-id", dNamesHeader, "write-seq.", sourceHeader, nameof(VfxStaticComboData.ConstantBufferBindInfoSlots), nameof(VfxStaticComboData.ConstantBufferBindInfoFlags), nameof(VfxShaderFile.HashMD5)];
            OutputFormatterTabulatedData tabulatedConfigFull = new(OutputWriter);
            tabulatedConfigFull.DefineHeaders(dConfigHeaders);

            for (var dBlockIndex = 0; dBlockIndex < StaticCombo.DynamicCombos.Length; dBlockIndex++)
            {
                var block = StaticCombo.DynamicCombos[dBlockIndex];
                var blockId = (int)block.DynamicComboId;
                var blockIndex = StaticCombo.GetDynamicComboIndex(block.DynamicComboId);
                if ((dBlockIndex + 1) % 100 == 0)
                {
                    tabulatedConfigFull.AddTabulatedRow(isVertexShader
                        ? ["", dNamesHeader, "", "", "", "", "", ""]
                        : ["", dNamesHeader, "", "", "", "", ""]);
                }
                var configIdText = $"0x{blockId:X2}";
                var configCombText = hasNoDConfigsDefined ? $"{"(default)",-14}" : tabbedConfigs.Pop();
                var writeSeqText = writeSequences[blockIndex] == -1 ? "[empty]" : $"SEQ[{writeSequences[blockIndex]}]";
                var blockSource = blockIdToSource.GetValueOrDefault(blockId);
                if (blockSource is null)
                {
                    return;
                }

                var sourceLink = $"{blockSource.ShaderFileId:X2}";
                // VShaderInputs is one entry per dynamic combo, indexed positionally.
                var vsInputs = isVertexShader && dBlockIndex < StaticCombo.VShaderInputs.Length
                    ? StaticCombo.VShaderInputs[dBlockIndex]
                    : -1;
                var gpuInputText = vsInputs >= 0 ? $"VS[{vsInputs}]" : "[none]";
                var arg1Text = $"{StaticCombo.ConstantBufferBindInfoSlots[blockIndex]}";
                var arg2Text = $"{StaticCombo.ConstantBufferBindInfoFlags[blockIndex]}";
                var hash = blockSource.HashMD5.ToString();
                tabulatedConfigFull.AddTabulatedRow(
                    isVertexShader
                    ? [configIdText, configCombText, writeSeqText, sourceLink, gpuInputText, arg1Text, arg2Text, hash]
                    : [configIdText, configCombText, writeSeqText, sourceLink, arg1Text, arg2Text, hash]);
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
                    if (psEndBlock.RasterizerStateDesc is { } rs)
                    {
                        OutputWriter.WriteLine("// Rasterizer State");
                        OutputWriter.WriteLine($"{nameof(rs.FillMode)}: {rs.FillMode}, {nameof(rs.CullMode)}: {rs.CullMode}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthClipEnable)}: {rs.DepthClipEnable}, {nameof(rs.MultisampleEnable)}: {rs.MultisampleEnable}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthBias)}: {rs.DepthBias}, {nameof(rs.DepthBiasClamp)}: {rs.DepthBiasClamp}, {nameof(rs.SlopeScaledDepthBias)}: {rs.SlopeScaledDepthBias}");
                    }
                    if (psEndBlock.DepthStencilStateDesc is { } ds)
                    {
                        OutputWriter.WriteLine("// Depth Stencil State");
                        OutputWriter.WriteLine($"{nameof(ds.DepthTestEnable)}: {ds.DepthTestEnable}, {nameof(ds.DepthWriteEnable)}: {ds.DepthWriteEnable}, {nameof(ds.DepthFunc)}: {ds.DepthFunc}");
                        OutputWriter.WriteLine($"{nameof(ds.StencilEnable)}: {ds.StencilEnable}, {nameof(ds.StencilReadMask)}: {ds.StencilReadMask}, {nameof(ds.StencilWriteMask)}: {ds.StencilWriteMask}, {nameof(ds.FrontStencilFailOp)}: {ds.FrontStencilFailOp}, {nameof(ds.FrontStencilDepthFailOp)}: {ds.FrontStencilDepthFailOp}");
                        OutputWriter.WriteLine($"{nameof(ds.FrontStencilPassOp)}: {ds.FrontStencilPassOp}, {nameof(ds.FrontStencilFunc)}: {ds.FrontStencilFunc}, {nameof(ds.BackStencilFailOp)}: {ds.BackStencilFailOp}, {nameof(ds.BackStencilDepthFailOp)}: {ds.BackStencilDepthFailOp}, {nameof(ds.BackStencilPassOp)}: {ds.BackStencilPassOp}");
                        OutputWriter.WriteLine($"{nameof(ds.BackStencilFunc)}: {ds.BackStencilFunc}");
                    }
                    if (psEndBlock.BlendStateDesc is { } bs)
                    {
                        OutputWriter.WriteLine("// Blend State");
                        OutputWriter.WriteLine($"{nameof(bs.AlphaToCoverageEnable)}: {bs.AlphaToCoverageEnable}, {nameof(bs.IndependentBlendEnable)}: {bs.IndependentBlendEnable}");
                        for (var i = 0; i < RsBlendStateDesc.MaxRenderTargets; i++)
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

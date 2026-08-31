using System.Diagnostics;
using System.IO;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Prints a summary of shader static combo data.
    /// </summary>
    public class PrintStaticComboSummary
    {
        private readonly IndentedTextWriter OutputWriter;
        private readonly VfxStaticComboData StaticCombo;

        /// <summary>
        /// Initializes a new instance and prints the summary.
        /// </summary>
        public PrintStaticComboSummary(VfxStaticComboData staticCombo, IndentedTextWriter outputWriter)
        {
            StaticCombo = staticCombo;
            OutputWriter = outputWriter;

            if (staticCombo.ParentProgramData?.VcsProgramType == VcsProgramType.Features)
            {
                return;
            }

            PrintConfigurationState();
            PrintAttributes();
            var (uniqueSequences, indexToSequence) = staticCombo.GetWriteSequences();
            PrintWriteSequences(uniqueSequences);
            PrintDynamicConfigurations(indexToSequence);
            OutputWriter.WriteLine();
            PrintSourceSummary();
            PrintRenderStateInfos();
        }

        private void PrintConfigurationState()
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            var configHeader = "PARENT STATIC COMBO CONFIGURATION";
            OutputWriter.WriteLine(configHeader);
            ComboConfigMapping configMapping = new(StaticCombo.ParentProgramData);
            var configState = configMapping.GetConfigState(StaticCombo.StaticComboId);
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

        private void PrintWriteSequences(List<VfxVariableIndexArray> uniqueSequences)
        {
            OutputWriter.WriteLine("DYNAMIC COMBO VARIABLES");

            OutputFormatterTabulatedData tabulatedData = new(OutputWriter);
            var emptyRow = new string[] { "", "", "", "", "" };
            tabulatedData.DefineHeaders(StaticCombo.AllVariables.Fields.Length > 0
                ? ["segment", "", nameof(VfxVariableIndexData.Dest), nameof(VfxVariableIndexData.Control), nameof(VfxVariableIndexData.LayoutSet)]
                : emptyRow);
            if (StaticCombo.AllVariables.Fields.Length > 0)
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

        private void PrintParamWriteSequence(VfxVariableIndexArray writeSequence, OutputFormatterTabulatedData tabulatedData)
        {
            PrintParamWriteSequenceSegment(writeSequence.Evaluated, 0, tabulatedData);
            PrintParamWriteSequenceSegment(writeSequence.RenderState, 1, tabulatedData);
            PrintParamWriteSequenceSegment(writeSequence.Constants, 2, tabulatedData);
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
                tabulatedData.AddTabulatedRow([i == 0 ? segmentDesc : string.Empty, paramDesc, destDesc, $"{controlDesc} ({field.RegisterOffset})", $"{field.LayoutSet,7}"]);
            }
        }

        private void PrintDynamicConfigurations(SortedDictionary<int, int> writeSequences)
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            var comboIdToShaderFile = GetComboIdToShaderFile(StaticCombo);
            var abbreviations = DynamicComboAbbreviations();
            var hasOnlyDefaultConfiguration = comboIdToShaderFile.Count == 1;
            var hasNoDynamicCombosDefined = abbreviations.Count == 0;
            var isVertexShader = StaticCombo.ParentProgramData.VcsProgramType == VcsProgramType.VertexShader;

            var configsDefined = hasOnlyDefaultConfiguration ? "" : $" ({comboIdToShaderFile.Count} defined)";
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

            foreach (var renderState in StaticCombo.DynamicComboRenderStates)
            {
                var dynamicComboConfig = StaticCombo.ParentProgramData.GetDynamicComboConfig(renderState.DynamicComboId);
                tabulatedConfigCombinations.AddTabulatedRow(IntArrayToStrings(dynamicComboConfig, nulledValue: 0));
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
            var dNamesHeader = hasNoDynamicCombosDefined ? "" : tabbedConfigs.Pop();
            var gpuSourceName = StaticCombo.ShaderFiles.Length > 0
                ? StaticCombo.ShaderFiles[0].SourceType.ToLowerInvariant()
                : "unknown";
            var sourceHeader = $"{gpuSourceName}-source";
            string[] dConfigHeaders = isVertexShader
                    ? ["config-id", dNamesHeader, "write-seq.", sourceHeader, "gpu-inputs", nameof(VfxStaticComboData.ConstantBufferBindingSlots), nameof(VfxStaticComboData.ConstantBufferBindingFlags), nameof(VfxShaderFile.HashMD5)]
                    : ["config-id", dNamesHeader, "write-seq.", sourceHeader, nameof(VfxStaticComboData.ConstantBufferBindingSlots), nameof(VfxStaticComboData.ConstantBufferBindingFlags), nameof(VfxShaderFile.HashMD5)];
            OutputFormatterTabulatedData tabulatedConfigFull = new(OutputWriter);
            tabulatedConfigFull.DefineHeaders(dConfigHeaders);

            for (var renderStateIndex = 0; renderStateIndex < StaticCombo.DynamicComboRenderStates.Length; renderStateIndex++)
            {
                var renderState = StaticCombo.DynamicComboRenderStates[renderStateIndex];
                var dynamicComboId = (int)renderState.DynamicComboId;
                var dynamicComboIndex = StaticCombo.GetDynamicComboIndex(renderState.DynamicComboId);
                if ((renderStateIndex + 1) % 100 == 0)
                {
                    tabulatedConfigFull.AddTabulatedRow(isVertexShader
                        ? ["", dNamesHeader, "", "", "", "", "", ""]
                        : ["", dNamesHeader, "", "", "", "", ""]);
                }
                var configIdText = $"0x{dynamicComboId:X2}";
                var configCombText = hasNoDynamicCombosDefined ? $"{"(default)",-14}" : tabbedConfigs.Pop();
                var writeSeqText = writeSequences[dynamicComboIndex] == -1 ? "[empty]" : $"SEQ[{writeSequences[dynamicComboIndex]}]";
                var shaderFile = comboIdToShaderFile.GetValueOrDefault(dynamicComboId);
                if (shaderFile is null)
                {
                    return;
                }

                var sourceLink = $"{shaderFile.ShaderFileId:X2}";
                // VsInputSignatureIndices is one entry per dynamic combo, indexed positionally.
                var vsInputs = isVertexShader && renderStateIndex < StaticCombo.VsInputSignatureIndices.Length
                    ? StaticCombo.VsInputSignatureIndices[renderStateIndex]
                    : -1;
                var gpuInputText = vsInputs >= 0 ? $"VS[{vsInputs}]" : "[none]";
                var bindSlotText = $"{StaticCombo.ConstantBufferBindingSlots[dynamicComboIndex]}";
                var bindFlagText = $"{StaticCombo.ConstantBufferBindingFlags[dynamicComboIndex]}";
                var hash = shaderFile.HashMD5.ToString();
                tabulatedConfigFull.AddTabulatedRow(
                    isVertexShader
                    ? [configIdText, configCombText, writeSeqText, sourceLink, gpuInputText, bindSlotText, bindFlagText, hash]
                    : [configIdText, configCombText, writeSeqText, sourceLink, bindSlotText, bindFlagText, hash]);
            }

            tabulatedConfigFull.PrintTabulatedValues();
            if (!hasNoDynamicCombosDefined)
            {
                OutputWriter.WriteLine();
            }
        }

        private List<(string, string)> DynamicComboAbbreviations()
        {
            Debug.Assert(StaticCombo.ParentProgramData != null);

            List<(string, string)> abbreviations = [];
            foreach (var dynamicCombo in StaticCombo.ParentProgramData.DynamicComboArray)
            {
                var abbreviation = ShortenShaderParam(dynamicCombo.Name).ToLowerInvariant();
                abbreviations.Add((dynamicCombo.Name, abbreviation));
            }
            return abbreviations;
        }

        static Dictionary<long, VfxShaderFile> GetComboIdToShaderFile(VfxStaticComboData staticCombo)
        {
            Dictionary<long, VfxShaderFile> comboIdToShaderFile = [];
            foreach (var renderState in staticCombo.DynamicComboRenderStates)
            {
                if (renderState.ShaderFileId != -1)
                {
                    comboIdToShaderFile.Add(renderState.DynamicComboId, staticCombo.ShaderFiles[renderState.ShaderFileId]);
                }
            }
            return comboIdToShaderFile;
        }

        private void PrintSourceSummary()
        {
            OutputWriter.WriteLine("source bytes/flags");
            OutputWriter.WriteLine($"{StaticCombo.ConstantBufferSize}      // Constant Buffer Size");
            OutputWriter.WriteLine($"{StaticCombo.StaticCB}       //");
            OutputWriter.WriteLine($"{StaticCombo.GlobalsBDA}       // added with v66");
            OutputWriter.WriteLine($"{StaticCombo.Flagbyte2}       //");
            OutputWriter.WriteLine();
            OutputWriter.WriteLine();
        }

        private void PrintRenderStateInfos()
        {
            OutputWriter.WriteLine("RENDER STATE INFO");
            OutputWriter.WriteLine();
            foreach (var renderState in StaticCombo.DynamicComboRenderStates)
            {
                OutputWriter.WriteLine($"block-ref         {renderState.DynamicComboId}");
                OutputWriter.WriteLine($"source-ref        {renderState.ShaderFileId}");
                OutputWriter.WriteLine($"source-pointer    {renderState.SourcePointer}");
                if (renderState is VfxRenderStateInfoHullShader hsRenderState)
                {
                    OutputWriter.WriteLine($"hs-arg            {hsRenderState.HullShaderArg}");
                }
                else if (renderState is VfxRenderStateInfoPixelShader psRenderState)
                {
                    if (psRenderState.RasterizerStateDesc is { } rs)
                    {
                        OutputWriter.WriteLine("// Rasterizer State");
                        OutputWriter.WriteLine($"{nameof(rs.FillMode)}: {rs.FillMode}, {nameof(rs.CullMode)}: {rs.CullMode}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthClipEnable)}: {rs.DepthClipEnable}, {nameof(rs.MultisampleEnable)}: {rs.MultisampleEnable}");
                        OutputWriter.WriteLine($"{nameof(rs.DepthBias)}: {rs.DepthBias}, {nameof(rs.DepthBiasClamp)}: {rs.DepthBiasClamp}, {nameof(rs.SlopeScaledDepthBias)}: {rs.SlopeScaledDepthBias}");
                    }
                    if (psRenderState.DepthStencilStateDesc is { } ds)
                    {
                        OutputWriter.WriteLine("// Depth Stencil State");
                        OutputWriter.WriteLine($"{nameof(ds.DepthTestEnable)}: {ds.DepthTestEnable}, {nameof(ds.DepthWriteEnable)}: {ds.DepthWriteEnable}, {nameof(ds.DepthFunc)}: {ds.DepthFunc}");
                        OutputWriter.WriteLine($"{nameof(ds.StencilEnable)}: {ds.StencilEnable}, {nameof(ds.StencilReadMask)}: {ds.StencilReadMask}, {nameof(ds.StencilWriteMask)}: {ds.StencilWriteMask}, {nameof(ds.FrontStencilFailOp)}: {ds.FrontStencilFailOp}, {nameof(ds.FrontStencilDepthFailOp)}: {ds.FrontStencilDepthFailOp}");
                        OutputWriter.WriteLine($"{nameof(ds.FrontStencilPassOp)}: {ds.FrontStencilPassOp}, {nameof(ds.FrontStencilFunc)}: {ds.FrontStencilFunc}, {nameof(ds.BackStencilFailOp)}: {ds.BackStencilFailOp}, {nameof(ds.BackStencilDepthFailOp)}: {ds.BackStencilDepthFailOp}, {nameof(ds.BackStencilPassOp)}: {ds.BackStencilPassOp}");
                        OutputWriter.WriteLine($"{nameof(ds.BackStencilFunc)}: {ds.BackStencilFunc}");
                    }
                    if (psRenderState.BlendStateDesc is { } bs)
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

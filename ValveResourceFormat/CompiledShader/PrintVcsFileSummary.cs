using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class PrintVcsFileSummary
    {
        private readonly OutputFormatterTabulatedData output;

        public PrintVcsFileSummary(VfxProgramData program, IndentedTextWriter outputWriter)
        {
            output = new OutputFormatterTabulatedData(outputWriter);
            if (program.VcsProgramType == VcsProgramType.Features)
            {
                PrintFeaturesHeader(program);
            }
            else
            {
                PrintPsVsHeader(program);
            }
            PrintCombos(program.StaticComboArray, "STATIC COMBOS");
            PrintComboRules(program, program.StaticComboRules, "STATIC COMBOS");
            PrintCombos(program.DynamicComboArray, "DYNAMIC COMBOS");
            PrintComboRules(program, program.DynamicComboRules, "DYNAMIC COMBOS");
            PrintParameters(program);
            PrintChannelBlocks(program);
            PrintBufferBlocks(program);
            PrintVertexSymbolBuffers(program);
            PrintZFrames(program);
        }

        private void PrintFeaturesHeader(VfxProgramData program)
        {
            Debug.Assert(program.FeaturesHeader != null);

            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {program.VcsVersion}");
            output.BreakLine();
            output.Write($"Showing {program.VcsProgramType}: {Path.GetFileName(program.FilenamePath)}");
            output.BreakLine();

            output.WriteLine($"VFX File Desc: {program.FeaturesHeader.FileDescription}");
            output.BreakLine();
            var ftHeader = program.FeaturesHeader;
            output.WriteLine($"{nameof(ftHeader.DevShader)} = {ftHeader.DevShader}");
            output.Write($"{nameof(ftHeader.AvailablePrograms)} = ");
            for (var i = 0; i < ftHeader.AvailablePrograms.Length; i++)
            {
                if (ftHeader.AvailablePrograms[i])
                {
                    output.Write($"{i}, ");
                }
            }
            output.BreakLine();
            output.WriteLine($"{nameof(program.VariableSourceMax)} = {program.VariableSourceMax}");
            output.BreakLine();
            output.WriteLine("Editor/Shader compiler stack");
            foreach (var v in program.HashesMD5)
            {
                output.WriteLine($"MD5    {v}");
            }
            output.BreakLine();
            if (ftHeader.Modes.Count == 0)
            {
                output.WriteLine("Primary modes");
                output.WriteLine("[default only]");
                return;
            }
            if (ftHeader.Modes.Count > 1)
            {
                output.WriteLine($"Primary static modes (one of these should be selected)");
            }
            else
            {
                output.WriteLine($"Primary static modes (this file has only one default mode)");
            }
            output.DefineHeaders(["name", "shader", "mode", "value"]);
            output.AddTabulatedRow(["----", "----", "----", "----"]);
            foreach (var mode in ftHeader.Modes)
            {
                var staticName = mode.StaticConfig.Length == 0 ? "(default)" : mode.StaticConfig;
                output.AddTabulatedRow([mode.Name, mode.Shader, staticName, BlankNegOne(mode.Value)]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintPsVsHeader(VfxProgramData program)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {program.VcsVersion}");
            output.BreakLine();
            output.Write($"Showing {program.VcsProgramType}: {Path.GetFileName(program.FilenamePath)}");
            output.BreakLine();
            output.WriteLine("Editor/Shader compiler stack");
            output.WriteLine($"MD5    {program.HashesMD5[0]}    // {program.VcsProgramType}");
            output.WriteLine($"MD5    {program.FileHash}    // Common editor/compiler hash shared by multiple different vcs files.");
            output.WriteLine($"{nameof(program.VariableSourceMax)} = {program.VariableSourceMax}");
            output.BreakLine();
        }

        private void PrintCombos(VfxCombo[] combos, string comboDesc)
        {
            if (combos.Length == 0)
            {
                return;
            }
            output.WriteLine($"{comboDesc}({combos.Length})");
            output.DefineHeaders([nameof(VfxCombo.BlockIndex), nameof(VfxCombo.Name), nameof(VfxCombo.RangeMin), nameof(VfxCombo.RangeMax), nameof(VfxCombo.ComboSourceType), nameof(VfxCombo.FeatureIndex), nameof(VfxCombo.ComboType), nameof(VfxCombo.Strings)]);
            foreach (var item in combos)
            {
                var checkboxNames = item.Strings.Length > 0
                    ? string.Join(", ", item.Strings.Select(static (x, i) => $"{i}=\"{x}\""))
                    : string.Empty;
                var comboSourceType = item.ComboType == VfxComboType.Dynamic ? ((VfxDynamicComboSourceType)item.ComboSourceType).ToString() : ((VfxStaticComboSourceType)item.ComboSourceType).ToString();
                output.AddTabulatedRow([$"[{item.BlockIndex,2}]", $"{item.Name}", $"{item.RangeMin}", $"{item.RangeMax}", $"{comboSourceType}", $"{item.FeatureIndex,2}", $"{item.ComboType}", checkboxNames]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintComboRules(VfxProgramData program, VfxRule[] vfxRules, string comboDesc)
        {
            if (vfxRules.Length == 0)
            {
                return;
            }

            output.WriteLine($"{comboDesc} INCLUSION/EXCLUSION RULES");

            foreach (var vfxRule in vfxRules)
            {
                var maxConstrains = Array.IndexOf(vfxRule.Indices, -1);

                var ruleName = new string[maxConstrains];
                for (var i = 0; i < ruleName.Length; i++)
                {
                    ruleName[i] = vfxRule.ConditionalTypes[i] switch
                    {
                        VfxRuleType.Unknown => string.Empty,
                        VfxRuleType.Dynamic => program.DynamicComboArray[vfxRule.Indices[i]].Name,
                        VfxRuleType.Static => program.StaticComboArray[vfxRule.Indices[i]].Name,
                        VfxRuleType.Feature => program.VcsProgramType == VcsProgramType.Features
                            ? program.StaticComboArray[vfxRule.Indices[i]].Name
                            : $"FEAT[{vfxRule.Indices[i]}]",
                        _ => throw new ShaderParserException($"Unknown {nameof(VfxRuleType)} {vfxRule.ConditionalTypes[i]}")
                    };
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(ruleName, BL);
                var s0 = $"[{vfxRule.BlockIndex,2}]";
                var s4 = $"{breakNames[0]}";
                var s5 = $"{vfxRule.Rule}{vfxRule.ExtraRuleData[0]}";
                var s6 = $"{CombineIntArray(vfxRule.Values[..maxConstrains])}";
                var s7 = $"{CombineIntArray(vfxRule.ExtraRuleData[..maxConstrains])}";
                output.WriteLine($"{s0}  {s5,-10}  {s4,-BL}{s6,-10}{s7,-8}");
                for (var i = 1; i < breakNames.Length; i++)
                {
                    output.WriteLine($"{"",-7}{"",-10}{"",-15}{"",-16}{breakNames[i],-BL}");
                }
            }
            output.BreakLine();
        }

        private void PrintParameters(VfxProgramData program)
        {
            if (program.VariableDescriptions.Length == 0)
            {
                output.WriteLine($"VARIABLE DESCRIPTIONS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            var dynExpCount = 0;
            var indexPad = program.VariableDescriptions.Length > 100 ? 3 : 2;
            // parameters
            output.WriteLine($"VARIABLE DESCRIPTIONS({program.VariableDescriptions.Length})    *dyn-expressions shown separately");
            output.DefineHeaders(["index",
                nameof(VfxVariableDescription.Name),
                nameof(VfxVariableDescription.VfxType),
                nameof(VfxVariableDescription.SourceIndex),
                nameof(VfxVariableDescription.ContextStateAffectedByVariable),
                nameof(VfxVariableDescription.MinPrecisionBits),
                nameof(VfxVariableDescription.RegisterElements),
                nameof(VfxVariableDescription.ExtConstantBufferId),
                nameof(VfxVariableDescription.VariableSource),
                nameof(VfxVariableDescription.StringData),
                nameof(VfxVariableDescription.RegisterType),
                nameof(VfxVariableDescription.UiType),
                nameof(VfxVariableDescription.UiGroup),
                "command 0|1",
                nameof(VfxVariableDescription.DefaultInputTexture),
                nameof(VfxVariableDescription.UiVisibilityExp)]);

            foreach (var param in program.VariableDescriptions)
            {
                var uiVisibilityExists = param.UiVisibilityExp.Length > 0 ? "true" : string.Empty;

                if (param.HasDynamicExpression || uiVisibilityExists.Length > 0)
                {
                    dynExpCount++;
                }

                var c0 = param.ImageSuffix;
                var c1 = param.ImageProcessor;
                if (c1.Length > 0)
                {
                    c0 += $" | {c1}";
                }
                output.AddTabulatedRow([$"[{("" + param.BlockIndex).PadLeft(indexPad)}]",
                    param.Name,
                    $"{param.VfxType}",
                    $"{BlankNegOne(param.SourceIndex),2}",
                    param.ContextStateAffectedByVariable.ToString(CultureInfo.InvariantCulture),
                    $"{BlankNegOne(param.MinPrecisionBits),2}",
                    $"{param.RegisterElements,2}",
                    param.ExtConstantBufferId.ToString(CultureInfo.InvariantCulture),
                    $"{param.VariableSource}",
                    param.StringData,
                    $"{param.RegisterType}",
                    param.UiType.ToString(),
                    param.UiGroup.CompactString,
                    $"{c0}",
                    $"{param.DefaultInputTexture}",
                    uiVisibilityExists]);
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();

            output.WriteLine("VARIABLES - Default values and limits    (type0,type1,arg0,arg1,arg2,arg4,arg5,command0 reprinted)");
            output.WriteLine("(- indicates -infinity, + indicates +infinity, def. = default)");
            output.DefineHeaders(["index",
                nameof(VfxVariableDescription.Name),
                nameof(VfxVariableDescription.IntDefs),
                nameof(VfxVariableDescription.IntMins),
                nameof(VfxVariableDescription.IntMaxs),
                nameof(VfxVariableDescription.FloatDefs),
                nameof(VfxVariableDescription.FloatMins),
                nameof(VfxVariableDescription.FloatMaxs),
                nameof(VfxVariableDescription.ChannelIndices),
                nameof(VfxVariableDescription.ImageFormat),
                nameof(VfxVariableDescription.ImageSuffix),
                nameof(VfxVariableDescription.DefaultInputTexture),
                nameof(VfxVariableDescription.DynExp),
                nameof(VfxVariableDescription.LayerId),
                nameof(VfxVariableDescription.AllowLayerOverride),
                nameof(VfxVariableDescription.MaxRes),
            ]);
            foreach (var param in program.VariableDescriptions)
            {
                var vfxType = GetVfxVariableTypeString(param.VfxType);
                var hasDynExp = param.HasDynamicExpression ? "true" : "";
                output.AddTabulatedRow([$"[{("" + param.BlockIndex).PadLeft(indexPad)}]",
                    $"{param.Name}",
                    $"{Comb(param.IntDefs)}",
                    $"{Comb(param.IntMins)}",
                    $"{Comb(param.IntMaxs)}",
                    $"{Comb(param.FloatDefs)}",
                    $"{Comb(param.FloatMins)}",
                    $"{Comb(param.FloatMaxs)}",
                    $"{Comb(param.ChannelIndices)}",
                    $"{param.ImageFormat}",
                    param.ImageSuffix,
                    param.DefaultInputTexture,
                    $"{hasDynExp}",
                    $"{param.LayerId}",
                    $"{param.AllowLayerOverride}",
                    $"{param.MaxRes}",
                ]);
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();

            if (dynExpCount > 0)
            {
                output.WriteLine($"DYNAMIC EXPRESSIONS({dynExpCount})");
                output.DefineHeaders(["param-index", "name", "vfxtype,registertype,vecsize,tex,", nameof(VfxVariableDescription.VariableSource), "dyn-exp", "ui-visibility"]);
                foreach (var param in program.VariableDescriptions)
                {
                    var dynExpstring = string.Empty;
                    var uiVisibilityString = string.Empty;

                    if (param.HasDynamicExpression)
                    {
                        dynExpstring = ParseDynamicExpression(param.DynExp);
                    }

                    if (param.UiVisibilityExp.Length > 0)
                    {
                        uiVisibilityString = ParseDynamicExpression(param.UiVisibilityExp);
                    }

                    if (dynExpstring.Length == 0 && uiVisibilityString.Length == 0 && param.VariableSource < VfxVariableSourceType.Viewport)
                    {
                        continue;
                    }

                    output.AddTabulatedRow([$"[{("" + param.BlockIndex).PadLeft(indexPad)}]",
                        $"{param.Name}",
                        $"{GetVfxVariableTypeString(param.VfxType)},{param.RegisterType,2},{param.RegisterElements,2},{BlankNegOne(param.SourceIndex),2}",
                        $"{param.VariableSource,2}",
                        dynExpstring,
                        uiVisibilityString]);
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
        }

        private void PrintChannelBlocks(VfxProgramData program)
        {
            output.WriteLine($"TEXTURE CHANNEL PROCESSORS({program.TextureChannelProcessors.Length})");
            if (program.TextureChannelProcessors.Length > 0)
            {
                output.DefineHeaders(["index", "name", nameof(VfxTextureChannelProcessor.Channel), "inputs", nameof(VfxTextureChannelProcessor.ColorMode)]);
            }
            else
            {
                output.DefineHeaders([]);
                output.WriteLine("[none defined]");
            }
            foreach (var channelBlock in program.TextureChannelProcessors)
            {
                var channelRemap = channelBlock.Channel.Indices.Select((ind, i) => ind != 0 && ind != i).Any(b => b)
                    ? $" [{string.Join(", ", channelBlock.Channel.Indices)}]"
                    : string.Empty;
                output.AddTabulatedRow([$"[{channelBlock.BlockIndex,2}]",
                    $"{channelBlock.TexProcessorName}",
                    channelBlock.Channel.ToString() + channelRemap,
                    string.Join(" ", channelBlock.InputTextureIndices),
                    $"{channelBlock.ColorMode,2}"]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintBufferBlocks(VfxProgramData program)
        {
            if (program.ExtConstantBufferDescriptions.Length == 0)
            {
                output.WriteLine("CONSTANT BUFFERS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var bufferBlock in program.ExtConstantBufferDescriptions)
            {
                output.WriteLine($"CONSTANT BUFFERS[{bufferBlock.BlockIndex}]");
                // valve splits bufferBlock.BufferSize into 0x7FFF and checks whether its negative
                output.WriteLine($"{bufferBlock.Name} size={bufferBlock.BufferSize} ({bufferBlock.BufferSize & 0x7FFF}) param-count={bufferBlock.Variables.Length}" +
                    $" arg0={bufferBlock.Type} crc32={bufferBlock.BlockCrc:x08}");
                output.DefineHeaders(["       ", "name", "offset", "vertex-size", "attrib-count", "data-count"]);
                foreach (var bufferParams in bufferBlock.Variables)
                {
                    var name = bufferParams.Name;
                    var bOffset = bufferParams.Offset;
                    var vectorSize = bufferParams.VectorSize;
                    var depth = bufferParams.Depth;
                    var length = bufferParams.Length;
                    output.AddTabulatedRow(["", $"{name}", $"{bOffset,3}", $"{vectorSize,3}", $"{depth,3}", $"{length,3}"]);

                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
        }

        private void PrintVertexSymbolBuffers(VfxProgramData program)
        {
            if (program.VSInputSignatures.Length == 0)
            {
                output.WriteLine("VERTEX INPUT SIGNATURES(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            // find best padding
            var namePad = 0;
            var typePad = 0;
            var optionPad = 0;
            foreach (var symbolBlock in program.VSInputSignatures)
            {
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    namePad = Math.Max(namePad, symbolsDef.Name.Length);
                    typePad = Math.Max(namePad, symbolsDef.Semantic.Length);
                    optionPad = Math.Max(namePad, symbolsDef.D3DSemanticName.Length);
                }
            }
            foreach (var symbolBlock in program.VSInputSignatures)
            {
                output.WriteLine($"VERTEX INPUT SIGNATURES[{symbolBlock.BlockIndex}] definitions={symbolBlock.SymbolsDefinition.Length}");
                output.DefineHeaders(["       ",
                    "Name".PadRight(namePad),
                    "Semantic".PadRight(typePad),
                    "SemanticName".PadRight(optionPad),
                    "Index"]);
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    output.AddTabulatedRow(["",
                        $"{symbolsDef.Name}",
                        $"{symbolsDef.Semantic}",
                        $"{symbolsDef.D3DSemanticName}",
                        $"{symbolsDef.D3DSemanticIndex,2}"]);
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintZFrames(VfxProgramData program)
        {
            var zframesHeader = $"STATIC COMBOS({program.StaticComboEntries.Count})";
            output.WriteLine(zframesHeader);
            if (program.StaticComboEntries.Count == 0)
            {
                var infoText = "";
                if (program.VcsProgramType == VcsProgramType.Features)
                {
                    infoText = "(Features files in general don't contain zframes)";
                }
                output.WriteLine($"[none defined] {infoText}");
                output.BreakLine();
                return;
            }
            // print the config headers every 100 frames
            var zframeCount = 0;
            // prepare the lookup to determine configuration state
            ConfigMappingParams configGen = new(program);
            // collect names in the order they appear
            List<string> sfNames = [];
            List<string> abbreviations = [];
            foreach (var sfBlock in program.StaticComboArray)
            {
                var sfShortName = ShortenShaderParam(sfBlock.Name).ToLowerInvariant();
                abbreviations.Add($"{sfBlock.Name}({sfShortName})");
                sfNames.Add(sfShortName);
            }
            var breakabbreviations = CombineValuesBreakString([.. abbreviations], 120);
            foreach (var abbr in breakabbreviations)
            {
                output.WriteLine(abbr);
            }
            if (abbreviations.Count > 0)
            {
                output.BreakLine();
            }

            var configHeader = CombineStringsSpaceSep([.. sfNames], 6);
            configHeader = $"{new string(' ', 16)}{configHeader}";
            foreach (var zframeDesc in program.StaticComboEntries)
            {
                if (zframeCount % 100 == 0 && configHeader.Trim().Length > 0)
                {
                    output.WriteLine($"{configHeader}");
                }
                var configState = configGen.GetConfigState(zframeDesc.Key);
                output.WriteLine($"  Z[{zframeDesc.Key:x08}] {CombineIntsSpaceSep(configState, 6)}");
                zframeCount++;
            }
        }

        private static string BlankNegOne(int val)
        {
            if (val == -1)
            {
                return "_";
            }
            return "" + val;
        }

        private static string Comb(int[] ints0)
        {
            return $"({Fmt(ints0[0])},{Fmt(ints0[1])},{Fmt(ints0[2])},{Fmt(ints0[3])})";
        }

        private static string Comb(float[] floats0)
        {
            return $"({Fmt(floats0[0])},{Fmt(floats0[1])},{Fmt(floats0[2])},{Fmt(floats0[3])})";
        }

        private static string Fmt(float val)
        {
            if (val == -VfxVariableDescription.FloatInf)
            {
                return "-";
            }

            if (val == VfxVariableDescription.FloatInf)
            {
                return "+";
            }

            return $"{val}";
        }

        private static string Fmt(int val)
        {
            if (val == -VfxVariableDescription.IntInf)
            {
                return "-";
            }

            if (val == VfxVariableDescription.IntInf)
            {
                return "+";
            }

            return "" + val;
        }
    }

}

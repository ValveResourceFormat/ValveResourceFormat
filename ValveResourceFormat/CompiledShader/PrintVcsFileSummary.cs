using System.Globalization;
using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class PrintVcsFileSummary
    {
        public delegate void HandleOutputWrite(string s);
        private readonly OutputFormatterTabulatedData output;

        public PrintVcsFileSummary(VfxProgramData program, HandleOutputWrite OutputWriter = null)
        {
            output = new OutputFormatterTabulatedData(OutputWriter);
            if (program.VcsProgramType == VcsProgramType.Features)
            {
                PrintFeaturesHeader(program);
                PrintFBlocks(program);
            }
            else
            {
                PrintPsVsHeader(program);
                PrintSBlocks(program);
            }
            PrintConstraints(program, program.StaticComboRules, "STATIC COMBOS");
            PrintDynamicConfigurations(program);
            PrintConstraints(program, program.DynamicComboRules, "DYNAMIC COMBOS");
            PrintParameters(program);
            PrintChannelBlocks(program);
            PrintBufferBlocks(program);
            PrintVertexSymbolBuffers(program);
            PrintZFrames(program);
        }

        private void PrintFeaturesHeader(VfxProgramData program)
        {
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

        private void PrintFBlocks(VfxProgramData program)
        {
            output.WriteLine($"FEATURES({program.StaticComboArray.Length})");
            if (program.StaticComboArray.Length == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(["index", "name", "nr-configs", "config-states", ""]);
            foreach (var item in program.StaticComboArray)
            {
                var configStates = "_";
                if (item.RangeMax > 0)
                {
                    configStates = "0";
                }
                for (var i = 1; i <= item.RangeMax; i++)
                {
                    configStates += $",{i}";
                }
                var configStates2 = "";
                if (item.RangeMax > 1)
                {
                    configStates2 = $"{CombineStringArray([.. item.CheckboxNames])}";
                }

                output.AddTabulatedRow([$"[{item.BlockIndex,2}]",
                    $"{item.Name}",
                    $"{item.RangeMax + 1}",
                    $"{configStates}",
                    $"{configStates2}"]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintSBlocks(VfxProgramData program)
        {
            output.WriteLine($"STATIC COMBOS({program.StaticComboArray.Length})");
            if (program.StaticComboArray.Length == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders([nameof(VfxCombo.BlockIndex), nameof(VfxCombo.Name), nameof(VfxCombo.RangeMax), nameof(VfxCombo.Arg3), nameof(VfxCombo.FeatureIndex)]);
            foreach (var item in program.StaticComboArray)
            {
                output.AddTabulatedRow([$"[{item.BlockIndex,2}]", $"{item.Name}", $"{item.RangeMax}", $"{item.Arg3}", $"{item.FeatureIndex,2}"]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintDynamicConfigurations(VfxProgramData program)
        {
            output.WriteLine($"DYNAMIC COMBOS({program.DynamicComboArray.Length})");
            if (program.DynamicComboArray.Length == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            int[] pad = [7, 40, 7, 7, 7];
            var h0 = "index";
            var h1 = "name";
            var h2 = "arg2";
            var h3 = "arg3";
            var h4 = "arg4";
            var blockHeader = $"{h0.PadRight(pad[0])} {h1.PadRight(pad[1])} {h2.PadRight(pad[2])} {h3.PadRight(pad[3])} {h4.PadRight(pad[4])}";
            output.WriteLine(blockHeader);
            foreach (var dBlock in program.DynamicComboArray)
            {
                var v0 = $"[{dBlock.BlockIndex,2}]";
                var v1 = dBlock.Name;
                var v2 = "" + dBlock.RangeMax;
                var v3 = "" + dBlock.Arg3;
                var v4 = $"{dBlock.FeatureIndex,2}";
                var blockSummary = $"{v0.PadRight(pad[0])} {v1.PadRight(pad[1])} {v2.PadRight(pad[2])} {v3.PadRight(pad[3])} {v4.PadRight(pad[4])}";
                output.WriteLine(blockSummary);
            }
            if (program.DynamicComboArray.Length == 0)
            {
                output.WriteLine("[empty list]");
            }
            output.BreakLine();
        }

        private void PrintConstraints(VfxProgramData program, VfxRule[] vfxRules, string comboDesc)
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
                        VfxRuleType.None => string.Empty,
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
                var s5 = $"{vfxRule.Rule}{vfxRule.Range2[0]}";
                var s6 = $"{CombineIntArray(vfxRule.Values[..maxConstrains])}";
                var s7 = $"{CombineIntArray(vfxRule.Range2[..maxConstrains])}";
                output.Write($"{s0}  {s5,-10}  {s4,-BL}{s6,-10}{s7,-8}");
                for (var i = 1; i < breakNames.Length; i++)
                {
                    output.Write($"\n{"",-7}{"",-10}{"",-15}{"",-16}{breakNames[i],-BL}");
                }
                output.BreakLine();
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
                nameof(VfxVariableDescription.Tex),
                nameof(VfxVariableDescription.Field1),
                nameof(VfxVariableDescription.Field2),
                nameof(VfxVariableDescription.VecSize),
                nameof(VfxVariableDescription.ExtConstantBufferId),
                nameof(VfxVariableDescription.VariableSource),
                nameof(VfxVariableDescription.StringData),
                nameof(VfxVariableDescription.RegisterType),
                nameof(VfxVariableDescription.UiType),
                nameof(VfxVariableDescription.UiGroup),
                "command 0|1",
                nameof(VfxVariableDescription.FileRef),
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
                    $"{BlankNegOne(param.Tex),2}",
                    param.Field1.ToString(CultureInfo.InvariantCulture),
                    $"{BlankNegOne(param.Field2),2}",
                    $"{param.VecSize,2}",
                    param.ExtConstantBufferId.ToString(CultureInfo.InvariantCulture),
                    $"{param.VariableSource}",
                    param.StringData,
                    $"{param.RegisterType}",
                    param.UiType.ToString(),
                    param.UiGroup.CompactString,
                    $"{c0}",
                    $"{param.FileRef}",
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
                nameof(VfxVariableDescription.FileRef),
                nameof(VfxVariableDescription.DynExp),
                nameof(VfxVariableDescription.Field3),
                nameof(VfxVariableDescription.Field4),
                nameof(VfxVariableDescription.Field5),
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
                    param.FileRef,
                    $"{hasDynExp}",
                    $"{param.Field3}",
                    $"{param.Field4}",
                    $"{param.Field5}",
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
                        $"{GetVfxVariableTypeString(param.VfxType)},{param.RegisterType,2},{param.VecSize,2},{BlankNegOne(param.Tex),2}",
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
            if (val == -1e9)
            {
                return "-";
            }

            if (val == 1e9)
            {
                return "+";
            }

            return $"{val}";
        }

        private static string Fmt(int val)
        {
            if (val == -999999999)
            {
                return "-";
            }

            if (val == 999999999)
            {
                return "+";
            }

            return "" + val;
        }
    }

}

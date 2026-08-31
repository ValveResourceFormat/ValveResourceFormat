using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    /// <summary>
    /// Prints a summary of VCS file contents.
    /// </summary>
    internal class PrintVcsFileSummary
    {
        private readonly OutputFormatterTabulatedData output;

        /// <summary>
        /// Initializes a new instance and prints the summary.
        /// </summary>
        public PrintVcsFileSummary(VfxProgramData program, IndentedTextWriter outputWriter, VfxProgramData? featuresProgram = null)
        {
            output = new OutputFormatterTabulatedData(outputWriter);
            var featureCombos = featuresProgram?.StaticComboArray;

            if (program.VcsProgramType == VcsProgramType.Features)
            {
                PrintFeaturesHeader(program);
            }
            else
            {
                PrintProgramHeader(program);
            }
            PrintCombos(program.StaticComboArray, "STATIC COMBOS", featureCombos);
            PrintComboRules(program, program.StaticComboRules, "STATIC COMBOS");
            PrintCombos(program.DynamicComboArray, "DYNAMIC COMBOS", featureCombos);
            PrintComboRules(program, program.DynamicComboRules, "DYNAMIC COMBOS");
            PrintVariableDescriptions(program);
            PrintTextureChannelProcessors(program);
            PrintConstantBuffers(program);
            PrintVsInputSignatures(program);
            PrintStaticCombos(program);
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
            output.WriteLine("Program hashes");
            foreach (var v in program.ProgramHashes)
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
                var staticName = mode.StaticComboName.Length == 0 ? "(default)" : mode.StaticComboName;
                output.AddTabulatedRow([mode.Name, mode.ShaderFallback, staticName, BlankNegOne(mode.StaticComboValue)]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintProgramHeader(VfxProgramData program)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {program.VcsVersion}");
            output.BreakLine();
            output.Write($"Showing {program.VcsProgramType}: {Path.GetFileName(program.FilenamePath)}");
            output.BreakLine();
            output.WriteLine("Program hashes");
            if (program.Resource is null)
            {
                output.WriteLine($"MD5    {program.ProgramHashes[0]}    // {program.VcsProgramType}");
            }
            else
            {
                foreach (var hash in program.ProgramHashes)
                {
                    output.WriteLine($"MD5    {hash}");
                }
            }
            output.WriteLine($"MD5    {program.VariableDescriptionVersionHash}    // {nameof(program.VariableDescriptionVersionHash)}, shared by multiple different vcs files.");
            output.WriteLine($"{nameof(program.VariableSourceMax)} = {program.VariableSourceMax}");
            output.BreakLine();
        }

        private void PrintCombos(VfxCombo[] combos, string comboDesc, VfxCombo[]? featureCombos)
        {
            if (combos.Length == 0)
            {
                return;
            }
            output.WriteLine($"{comboDesc}({combos.Length})");
            output.DefineHeaders([nameof(VfxCombo.Index), nameof(VfxCombo.Name), nameof(VfxCombo.RangeMin), nameof(VfxCombo.RangeMax), nameof(VfxCombo.ComboSourceType), nameof(VfxCombo.FeatureIndex), nameof(VfxCombo.ComboType), nameof(VfxCombo.StateNames)]);
            foreach (var item in combos)
            {
                var stateNames = item.StateNames.Length > 0
                    ? string.Join(", ", item.StateNames.Select(static (x, i) => $"{i}=\"{x}\""))
                    : string.Empty;
                var comboSourceType = item.ComboType == VfxComboType.Dynamic ? ((VfxDynamicComboSourceType)item.ComboSourceType).ToString() : ((VfxStaticComboSourceType)item.ComboSourceType).ToString();
                var featureIndex = $"{item.FeatureIndex,2}";

                if (item.FeatureIndex >= 0 && featureCombos != null && item.FeatureIndex < featureCombos.Length)
                {
                    var feature = featureCombos[item.FeatureIndex];
                    featureIndex += $" ({feature.Name} {feature.RangeMin}..{feature.RangeMax})";
                }
                output.AddTabulatedRow([$"[{item.Index,2}]", $"{item.Name}", $"{item.RangeMin}", $"{item.RangeMax}", $"{comboSourceType}", featureIndex, $"{item.ComboType}", stateNames]);
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
                var argCount = Array.IndexOf(vfxRule.ArgIndices, -1);
                if (argCount < 0)
                {
                    argCount = vfxRule.ArgIndices.Length;
                }

                var ruleName = new string[argCount];
                for (var i = 0; i < ruleName.Length; i++)
                {
                    ruleName[i] = vfxRule.ArgTypes[i] switch
                    {
                        VfxRuleType.Unknown => string.Empty,
                        VfxRuleType.Dynamic => program.DynamicComboArray[vfxRule.ArgIndices[i]].Name,
                        VfxRuleType.Static => program.StaticComboArray[vfxRule.ArgIndices[i]].Name,
                        VfxRuleType.Feature => program.VcsProgramType == VcsProgramType.Features
                            ? program.StaticComboArray[vfxRule.ArgIndices[i]].Name
                            : $"FEAT[{vfxRule.ArgIndices[i]}]",
                        _ => throw new ShaderParserException($"Unknown {nameof(VfxRuleType)} {vfxRule.ArgTypes[i]}")
                    };
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(ruleName, BL);
                var indexText = $"[{vfxRule.Index,2}]";
                var namesText = $"{breakNames[0]}";
                var methodText = $"{vfxRule.RuleMethod}{vfxRule.ExtraRuleData[0]}";
                var valuesText = $"{CombineIntArray(vfxRule.ArgValues[..argCount])}";
                var extraDataText = $"{CombineIntArray(vfxRule.ExtraRuleData[..argCount])}";
                output.WriteLine($"{indexText}  {methodText,-10}  {namesText,-BL}{valuesText,-10}{extraDataText,-8}");
                for (var i = 1; i < breakNames.Length; i++)
                {
                    output.WriteLine($"{"",-7}{"",-10}{"",-15}{"",-16}{breakNames[i],-BL}");
                }
            }
            output.BreakLine();
        }

        private void PrintVariableDescriptions(VfxProgramData program)
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
            output.WriteLine($"VARIABLE DESCRIPTIONS({program.VariableDescriptions.Length})    *dyn-expressions shown separately");
            output.DefineHeaders(["index",
                nameof(VfxVariableDescription.Name),
                nameof(VfxVariableDescription.VfxType),
                nameof(VfxVariableDescription.SourceIndex),
                nameof(VfxVariableDescription.ContextStateAffectedByVariable),
                nameof(VfxVariableDescription.MinPrecisionBits),
                nameof(VfxVariableDescription.RegisterElements),
                nameof(VfxVariableDescription.TypeSpecificBits),
                nameof(VfxVariableDescription.VariableSource),
                nameof(VfxVariableDescription.SourceString),
                nameof(VfxVariableDescription.RegisterType),
                nameof(VfxVariableDescription.UiType),
                nameof(VfxVariableDescription.UiGroup),
                "file-ending | command",
                nameof(VfxVariableDescription.DefaultInputTexture),
                nameof(VfxVariableDescription.UiVisibilityExpression)]);

            foreach (var param in program.VariableDescriptions)
            {
                var uiVisibilityExists = param.UiVisibilityExpression.Length > 0 ? "true" : string.Empty;

                if (param.HasDynamicExpression || uiVisibilityExists.Length > 0)
                {
                    dynExpCount++;
                }

                var c0 = param.TextureFileEnding;
                var c1 = param.InputProcessingCommand;
                if (c1.Length > 0)
                {
                    c0 += $" | {c1}";
                }
                output.AddTabulatedRow([$"[{("" + param.Index).PadLeft(indexPad)}]",
                    param.Name,
                    $"{param.VfxType}",
                    $"{BlankNegOne(param.SourceIndex),2}",
                    param.ContextStateAffectedByVariable.ToString(CultureInfo.InvariantCulture),
                    $"{BlankNegOne(param.MinPrecisionBits),2}",
                    $"{param.RegisterElements,2}",
                    param.TypeSpecificBits.ToString(CultureInfo.InvariantCulture),
                    $"{param.VariableSource}",
                    param.SourceString,
                    $"{param.RegisterType}",
                    param.UiType.ToString(),
                    param.UiGroup.CompactString,
                    $"{c0}",
                    $"{param.DefaultInputTexture}",
                    uiVisibilityExists]);
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();

            output.WriteLine("VARIABLES - Default values and limits");
            output.WriteLine("(- indicates -infinity, + indicates +infinity, def. = default)");
            output.DefineHeaders(["index",
                nameof(VfxVariableDescription.Name),
                nameof(VfxVariableDescription.IntDefs),
                nameof(VfxVariableDescription.IntMins),
                nameof(VfxVariableDescription.IntMaxs),
                nameof(VfxVariableDescription.FloatDefs),
                nameof(VfxVariableDescription.FloatMins),
                nameof(VfxVariableDescription.FloatMaxs),
                nameof(VfxVariableDescription.ChannelInfoIndices),
                nameof(VfxVariableDescription.OutputTextureFormat),
                nameof(VfxVariableDescription.TextureFileEnding),
                nameof(VfxVariableDescription.DefaultInputTexture),
                nameof(VfxVariableDescription.CompiledExpression),
                nameof(VfxVariableDescription.LayerId),
                nameof(VfxVariableDescription.AllowLayerOverride),
                nameof(VfxVariableDescription.MaxRes),
            ]);
            foreach (var param in program.VariableDescriptions)
            {
                var vfxType = GetVfxVariableTypeString(param.VfxType);
                var hasDynExp = param.HasDynamicExpression ? "true" : "";
                output.AddTabulatedRow([$"[{("" + param.Index).PadLeft(indexPad)}]",
                    $"{param.Name}",
                    $"{Comb(param.IntDefs)}",
                    $"{Comb(param.IntMins)}",
                    $"{Comb(param.IntMaxs)}",
                    $"{Comb(param.FloatDefs)}",
                    $"{Comb(param.FloatMins)}",
                    $"{Comb(param.FloatMaxs)}",
                    $"{Comb(param.ChannelInfoIndices)}",
                    $"{param.OutputTextureFormat}",
                    param.TextureFileEnding,
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
                        dynExpstring = ParseDynamicExpression(param.CompiledExpression);
                    }

                    if (param.UiVisibilityExpression.Length > 0)
                    {
                        uiVisibilityString = ParseDynamicExpression(param.UiVisibilityExpression);
                    }

                    if (dynExpstring.Length == 0 && uiVisibilityString.Length == 0 && param.VariableSource < VfxVariableSourceType.Viewport)
                    {
                        continue;
                    }

                    output.AddTabulatedRow([$"[{("" + param.Index).PadLeft(indexPad)}]",
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

        private void PrintTextureChannelProcessors(VfxProgramData program)
        {
            output.WriteLine($"TEXTURE CHANNEL PROCESSORS({program.TextureChannelProcessors.Length})");
            if (program.TextureChannelProcessors.Length > 0)
            {
                output.DefineHeaders(["index", "name", nameof(VfxTextureChannelProcessor.Channel), "inputs", nameof(VfxTextureChannelProcessor.OutputColorSpace)]);
            }
            else
            {
                output.DefineHeaders([]);
                output.WriteLine("[none defined]");
            }
            foreach (var channelProcessor in program.TextureChannelProcessors)
            {
                var destinations = channelProcessor.Channel.Destinations;
                var channelRemap = destinations.Where((destination, i) => destination != i).Any()
                    ? $" [{string.Join(", ", destinations)}]"
                    : string.Empty;
                output.AddTabulatedRow([$"[{channelProcessor.Index,2}]",
                    $"{channelProcessor.MipProcessingCommand}",
                    channelProcessor.Channel.ToString() + channelRemap,
                    string.Join(" ", channelProcessor.InputTextureIndices),
                    $"{channelProcessor.OutputColorSpace,2}"]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintConstantBuffers(VfxProgramData program)
        {
            if (program.ExtConstantBufferDescriptions.Length == 0)
            {
                output.WriteLine("CONSTANT BUFFERS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var buffer in program.ExtConstantBufferDescriptions)
            {
                output.WriteLine($"CONSTANT BUFFERS[{buffer.Index}]");
                var pushConstant = buffer.IsPushConstantBuffer ? " push-constant" : string.Empty;
                output.WriteLine($"{buffer.Name} size={buffer.BufferSize}{pushConstant} param-count={buffer.Variables.Length}" +
                    $" type={buffer.Type} crc32={buffer.BlockCrc:x08}");
                output.DefineHeaders(["       ", "name", "offset", "vector-size", "rows", "elements"]);
                foreach (var variable in buffer.Variables)
                {
                    output.AddTabulatedRow(["", $"{variable.Name}", $"{variable.Offset,3}", $"{variable.VectorSize,3}", $"{variable.RowCount,3}", $"{variable.ElementCount,3}"]);
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
        }

        private void PrintVsInputSignatures(VfxProgramData program)
        {
            if (program.VsInputSignatures.Length == 0)
            {
                output.WriteLine("VERTEX INPUT SIGNATURES(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            // find best padding
            var namePad = 0;
            var semanticPad = 0;
            var d3dSemanticPad = 0;
            foreach (var inputSignature in program.VsInputSignatures)
            {
                foreach (var element in inputSignature.Elements)
                {
                    namePad = Math.Max(namePad, element.Name.Length);
                    semanticPad = Math.Max(semanticPad, element.Semantic.Length);
                    d3dSemanticPad = Math.Max(d3dSemanticPad, element.D3DSemanticName.Length);
                }
            }
            foreach (var inputSignature in program.VsInputSignatures)
            {
                output.WriteLine($"VERTEX INPUT SIGNATURES[{inputSignature.Index}] definitions={inputSignature.Elements.Length}");
                output.DefineHeaders(["       ",
                    "Name".PadRight(namePad),
                    "Semantic".PadRight(semanticPad),
                    "SemanticName".PadRight(d3dSemanticPad),
                    "Index"]);
                foreach (var element in inputSignature.Elements)
                {
                    output.AddTabulatedRow(["",
                        $"{element.Name}",
                        $"{element.Semantic}",
                        $"{element.D3DSemanticName}",
                        $"{element.D3DSemanticIndex,2}"]);
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintStaticCombos(VfxProgramData program)
        {
            var staticCombosHeader = $"STATIC COMBOS({program.StaticComboEntries.Count})";
            output.WriteLine(staticCombosHeader);
            if (program.StaticComboEntries.Count == 0)
            {
                var infoText = "";
                if (program.VcsProgramType == VcsProgramType.Features)
                {
                    infoText = "(Features files in general don't contain static combos)";
                }
                output.WriteLine($"[none defined] {infoText}");
                output.BreakLine();
                return;
            }
            // print the config headers every 100 static combos
            var staticComboCount = 0;
            // prepare the lookup to determine configuration state
            ComboConfigMapping configMapping = new(program);
            // collect names in the order they appear
            List<string> comboNames = [];
            List<string> abbreviations = [];
            foreach (var staticCombo in program.StaticComboArray)
            {
                var shortName = ShortenShaderParam(staticCombo.Name).ToLowerInvariant();
                abbreviations.Add($"{staticCombo.Name}({shortName})");
                comboNames.Add(shortName);
            }
            var abbreviationLines = CombineValuesBreakString([.. abbreviations], 120);
            foreach (var abbr in abbreviationLines)
            {
                output.WriteLine(abbr);
            }
            if (abbreviations.Count > 0)
            {
                output.BreakLine();
            }

            var configHeader = CombineStringsSpaceSep([.. comboNames], 6);
            configHeader = $"{new string(' ', 16)}{configHeader}";
            foreach (var staticComboEntry in program.StaticComboEntries)
            {
                if (staticComboCount % 100 == 0 && configHeader.Trim().Length > 0)
                {
                    output.WriteLine($"{configHeader}");
                }
                var configState = configMapping.GetConfigState(staticComboEntry.Key);
                output.WriteLine($"  SC[{staticComboEntry.Key:x08}] {CombineIntsSpaceSep(configState, 6)}");
                staticComboCount++;
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

        private static string Comb(int[] values)
        {
            return $"({Fmt(values[0])},{Fmt(values[1])},{Fmt(values[2])},{Fmt(values[3])})";
        }

        private static string Comb(float[] values)
        {
            return $"({Fmt(values[0])},{Fmt(values[1])},{Fmt(values[2])},{Fmt(values[3])})";
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

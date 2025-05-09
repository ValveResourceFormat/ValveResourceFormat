using System.Globalization;
using System.IO;
using System.Linq;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

#nullable disable

namespace ValveResourceFormat.CompiledShader
{
    public class PrintVcsFileSummary
    {
        private readonly OutputFormatterTabulatedData output;
        private readonly bool showRichTextBoxLinks;
        private readonly List<string> relatedFiles;

        public PrintVcsFileSummary(ShaderFile shaderFile, HandleOutputWrite OutputWriter = null,
            bool showRichTextBoxLinks = false, List<string> relatedFiles = null)
        {
            this.showRichTextBoxLinks = showRichTextBoxLinks;
            this.relatedFiles = relatedFiles;

            output = new OutputFormatterTabulatedData(OutputWriter);
            if (shaderFile.VcsProgramType == VcsProgramType.Features)
            {
                PrintFeaturesHeader(shaderFile);
                PrintFBlocks(shaderFile);
            }
            else
            {
                PrintPsVsHeader(shaderFile);
                PrintSBlocks(shaderFile);
            }
            PrintStaticConstraints(shaderFile);
            PrintDynamicConfigurations(shaderFile);
            PrintDynamicConstraints(shaderFile);
            PrintParameters(shaderFile);
            PrintChannelBlocks(shaderFile);
            PrintBufferBlocks(shaderFile);
            PrintVertexSymbolBuffers(shaderFile);
            PrintZFrames(shaderFile);
        }

        private void PrintFeaturesHeader(ShaderFile shaderFile)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.VcsVersion}");
            output.BreakLine();
            output.Write($"Showing {shaderFile.VcsProgramType}: {Path.GetFileName(shaderFile.FilenamePath)}");
            if (showRichTextBoxLinks)
            {
                output.WriteLine($" (view byte detail \\\\{Path.GetFileName(shaderFile.FilenamePath)}\\bytes)");
            }
            else
            {
                output.BreakLine();
            }
            if (showRichTextBoxLinks && relatedFiles != null && relatedFiles.Count > 1)
            {
                output.Write("Related files:");
                foreach (var relatedFile in relatedFiles)
                {
                    output.Write($" \\\\{relatedFile.Replace("/", "\\", StringComparison.Ordinal)}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine($"VFX File Desc: {shaderFile.FeaturesHeader.FileDescription}");
            output.BreakLine();
            output.WriteLine($"has additional file = {shaderFile.AdditionalFiles}");
            var ftHeader = shaderFile.FeaturesHeader;
            if (ftHeader.AdditionalFileFlags.Length > 0)
            {
                output.WriteLine($"additional file bool flags ({string.Join(",", ftHeader.AdditionalFileFlags)})");
            }
            output.WriteLine($"{nameof(shaderFile.FeaturesHeader.Version)} = {shaderFile.FeaturesHeader.Version}");
            output.WriteLine($"{nameof(ftHeader.DevShader)} = {ftHeader.DevShader}");
            output.WriteLine($"present files features={ftHeader.FeaturesFileFlags}, vs={ftHeader.VertexFileFlags}, ps={ftHeader.PixelFileFlags}, " +
                $"gs={ftHeader.GeometryFileFlags}, cs={ftHeader.ComputeFileFlags}, hs={ftHeader.HullFileFlags}, ds={ftHeader.DomainFileFlags}");
            output.WriteLine($"possible editor description = {shaderFile.VariableSourceMax}");
            output.BreakLine();
            output.WriteLine("Editor/Shader compiler stack");
            foreach (var v in shaderFile.EditorIDs)
            {
                output.WriteLine($"MD5    {v.Item1}    {v.Item2}");
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

        private void PrintPsVsHeader(ShaderFile shaderFile)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.VcsVersion}");
            output.BreakLine();
            output.Write($"Showing {shaderFile.VcsProgramType}: {Path.GetFileName(shaderFile.FilenamePath)}");
            if (showRichTextBoxLinks)
            {
                output.WriteLine($" (view byte detail \\\\{Path.GetFileName(shaderFile.FilenamePath)}\\bytes)");
            }
            else
            {
                output.BreakLine();
            }
            if (showRichTextBoxLinks && relatedFiles != null && relatedFiles.Count > 1)
            {
                output.Write("Related files:");
                foreach (var relatedFile in relatedFiles)
                {
                    output.Write($" \\\\{relatedFile.Replace("/", "\\", StringComparison.Ordinal)}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine("Editor/Shader compiler stack");
            output.WriteLine($"MD5    {shaderFile.EditorIDs[0]}    // {shaderFile.VcsProgramType}");
            output.WriteLine($"MD5    {shaderFile.FileHash}    // Common editor/compiler hash shared by multiple different vcs files.");
            output.WriteLine($"possible editor description = {shaderFile.VariableSourceMax}");
            output.BreakLine();
        }

        private void PrintFBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"FEATURES({shaderFile.StaticCombos.Count})");
            if (shaderFile.StaticCombos.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(["index", "name", "nr-configs", "config-states", ""]);
            foreach (var item in shaderFile.StaticCombos)
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

        private void PrintSBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"STATIC-CONFIGURATIONS({shaderFile.StaticCombos.Count})");
            if (shaderFile.StaticCombos.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders([nameof(VfxCombo.BlockIndex), nameof(VfxCombo.Name), nameof(VfxCombo.RangeMax), nameof(VfxCombo.Arg3), nameof(VfxCombo.FeatureIndex)]);
            foreach (var item in shaderFile.StaticCombos)
            {
                output.AddTabulatedRow([$"[{item.BlockIndex,2}]", $"{item.Name}", $"{item.RangeMax}", $"{item.Arg3}", $"{item.FeatureIndex,2}"]);
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintStaticConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("STATIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.StaticComboRules.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var sfRuleBlock in shaderFile.StaticComboRules)
            {
                var sfNames = new string[sfRuleBlock.Indices.Length];
                for (var i = 0; i < sfNames.Length; i++)
                {
                    sfNames[i] = shaderFile.StaticCombos[sfRuleBlock.Indices[i]].Name;
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(sfNames, BL);
                var s0 = $"[{sfRuleBlock.BlockIndex,2}]";
                var s4 = $"{breakNames[0]}";
                var s5 = $"{sfRuleBlock.Rule}{sfRuleBlock.Range2[0]}";
                var s6 = $"{CombineIntArray(sfRuleBlock.Values)}";
                var s7 = $"{CombineIntArray(sfRuleBlock.Range2)}";
                output.Write($"{s0}  {s5,-10}  {s4,-BL}{s6,-10}{s7,-8}");
                for (var i = 1; i < breakNames.Length; i++)
                {
                    output.Write($"\n{"",7}{"",10}{"",16}{breakNames[i],-BL}{sfRuleBlock.Description,-BL}");
                }
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintDynamicConfigurations(ShaderFile shaderFile)
        {
            output.WriteLine($"DYNAMIC-CONFIGURATIONS({shaderFile.DynamicCombos.Count})");
            if (shaderFile.DynamicCombos.Count == 0)
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
            foreach (var dBlock in shaderFile.DynamicCombos)
            {
                var v0 = $"[{dBlock.BlockIndex,2}]";
                var v1 = dBlock.Name;
                var v2 = "" + dBlock.RangeMax;
                var v3 = "" + dBlock.Arg3;
                var v4 = $"{dBlock.FeatureIndex,2}";
                var blockSummary = $"{v0.PadRight(pad[0])} {v1.PadRight(pad[1])} {v2.PadRight(pad[2])} {v3.PadRight(pad[3])} {v4.PadRight(pad[4])}";
                output.WriteLine(blockSummary);
            }
            if (shaderFile.DynamicCombos.Count == 0)
            {
                output.WriteLine("[empty list]");
            }
            output.BreakLine();
        }

        private void PrintDynamicConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("DYNAMIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.DynamicComboRules.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var dRuleBlock in shaderFile.DynamicComboRules)
            {
                var dRuleName = new string[dRuleBlock.ConditionalTypes.Length];
                for (var i = 0; i < dRuleName.Length; i++)
                {
                    dRuleName[i] = dRuleBlock.ConditionalTypes[i] switch
                    {
                        ConditionalType.Dynamic => shaderFile.DynamicCombos[dRuleBlock.Indices[i]].Name,
                        ConditionalType.Static => shaderFile.StaticCombos[dRuleBlock.Indices[i]].Name,
                        ConditionalType.Feature => throw new InvalidOperationException("Dynamic combos can't be constrained by features!"),
                        _ => throw new ShaderParserException($"Unknown {nameof(ConditionalType)} {dRuleBlock.ConditionalTypes[i]}")
                    };
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(dRuleName, BL);
                var s0 = $"[{dRuleBlock.BlockIndex,2}]";
                var s4 = $"{breakNames[0]}";
                var s5 = $"{dRuleBlock.Rule}{dRuleBlock.Range2[0]}";
                var s6 = $"{CombineIntArray(dRuleBlock.Values)}";
                var s7 = $"{CombineIntArray(dRuleBlock.Range2)}";
                output.Write($"{s0}  {s5,-10}  {s4,-BL}{s6,-10}{s7,-8}");
                for (var i = 1; i < breakNames.Length; i++)
                {
                    output.Write($"\n{"",-7}{"",-10}{"",-15}{"",-16}{breakNames[i],-BL}");
                }
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintParameters(ShaderFile shaderFile)
        {
            if (shaderFile.VariableDescriptions.Count == 0)
            {
                output.WriteLine($"PARAMETERS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            var dynExpCount = 0;
            var indexPad = shaderFile.VariableDescriptions.Count > 100 ? 3 : 2;
            // parameters
            output.WriteLine($"PARAMETERS({shaderFile.VariableDescriptions.Count})    *dyn-expressions shown separately");
            output.DefineHeaders(["index",
                nameof(VfxVariableDescription.Name),
                nameof(VfxVariableDescription.VfxType),
                nameof(VfxVariableDescription.Res0),
                nameof(VfxVariableDescription.Tex),
                nameof(VfxVariableDescription.Arg3),
                nameof(VfxVariableDescription.Arg4),
                nameof(VfxVariableDescription.Arg12),
                nameof(VfxVariableDescription.Arg5),
                nameof(VfxVariableDescription.Arg6),
                nameof(VfxVariableDescription.VecSize),
                nameof(VfxVariableDescription.Id),
                nameof(VfxVariableDescription.Arg9),
                nameof(VfxVariableDescription.Arg10),
                nameof(VfxVariableDescription.Arg11),
                "dyn-exp*",
                nameof(VfxVariableDescription.StringData),
                nameof(VfxVariableDescription.Lead0),
                nameof(VfxVariableDescription.ParamType),
                nameof(VfxVariableDescription.UiType),
                nameof(VfxVariableDescription.UiGroup),
                "command 0|1",
                nameof(VfxVariableDescription.FileRef),
                nameof(VfxVariableDescription.UiVisibilityExp)]);

            foreach (var param in shaderFile.VariableDescriptions)
            {
                var dynExpExists = param.HasDynamicExpression ? "true" : string.Empty;
                var uiVisibilityExists = param.UiVisibilityExp.Length > 0 ? "true" : string.Empty;

                if (dynExpExists.Length > 0 || uiVisibilityExists.Length > 0)
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
                    $"{param.Res0}",
                    $"{BlankNegOne(param.Tex),2}",
                    param.Arg3.ToString(CultureInfo.InvariantCulture),
                    param.Arg4.ToString(CultureInfo.InvariantCulture),
                    $"{BlankNegOne(param.Arg12),2}",
                    param.Arg5.ToString(CultureInfo.InvariantCulture),
                    param.Arg6.ToString(CultureInfo.InvariantCulture),
                    $"{param.VecSize,2}",
                    param.Id.ToString(CultureInfo.InvariantCulture),
                    param.Arg9.ToString(CultureInfo.InvariantCulture),
                    param.Arg10.ToString(CultureInfo.InvariantCulture),
                    param.Arg11.ToString(CultureInfo.InvariantCulture),
                    dynExpExists,
                    param.StringData,
                    $"{param.Lead0}",
                    $"{param.ParamType}",
                    param.UiType.ToString(),
                    param.UiGroup.CompactString,
                    $"{c0}",
                    $"{param.FileRef}",
                    uiVisibilityExists]);
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();
            if (dynExpCount == 0)
            {
                output.WriteLine($"DYNAMIC EXPRESSIONS({dynExpCount})");
                output.WriteLine("[none defined]");
            }
            else
            {
                output.WriteLine($"DYNAMIC EXPRESSIONS({dynExpCount})    (name0,type0,type1,arg0,arg1,arg2,arg4,arg5 reprinted)");
                output.DefineHeaders(["param-index", "name0", "t0,t1,a0,a1,a2,a4,a5  ", "dyn-exp", "ui-visibility"]);
                foreach (var param in shaderFile.VariableDescriptions)
                {
                    var dynExpstring = string.Empty;
                    var uiVisibilityString = string.Empty;

                    if (param.Lead0.HasFlag(LeadFlags.Dynamic))
                    {
                        dynExpstring = param.Lead0.HasFlag(LeadFlags.DynMaterial)
                            ? "< shader id >"
                            : param.HasDynamicExpression
                                ? ParseDynamicExpression(param.DynExp)
                                : "< empty >";
                    }

                    if (param.UiVisibilityExp.Length > 0)
                    {
                        uiVisibilityString = ParseDynamicExpression(param.UiVisibilityExp);
                    }

                    if (dynExpstring.Length == 0 && uiVisibilityString.Length == 0)
                    {
                        continue;
                    }

                    output.AddTabulatedRow([$"[{("" + param.BlockIndex).PadLeft(indexPad)}]",
                        $"{param.Name}",
                        $"{param.UiType,2},{param.Lead0,2},{BlankNegOne(param.Tex),2},{Vfx.GetTypeName(param.VfxType)},{param.ParamType,2},{param.VecSize,2},{param.Id}",
                        dynExpstring,
                        uiVisibilityString]);
                }
                output.PrintTabulatedValues();
            }
            output.BreakLine();
            output.WriteLine("PARAMETERS - Default values and limits    (type0,type1,arg0,arg1,arg2,arg4,arg5,command0 reprinted)");
            output.WriteLine("(- indicates -infinity, + indicates +infinity, def. = default)");
            output.DefineHeaders(["index",
                "name0",
                "t0,t1,a0,a1,a2,a4,a5  ",
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
                nameof(VfxVariableDescription.V65Data)]);
            foreach (var param in shaderFile.VariableDescriptions)
            {
                var vfxType = Vfx.GetTypeName(param.VfxType);
                var hasDynExp = param.HasDynamicExpression ? "true" : "";
                output.AddTabulatedRow([$"[{("" + param.BlockIndex).PadLeft(indexPad)}]",
                    $"{param.Name}",
                    $"{param.UiType,2},{param.Lead0,2},{BlankNegOne(param.Tex),2},{vfxType},{param.ParamType,2},{param.VecSize,2},{param.Id}",
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
                    BitConverter.ToString(param.V65Data)]);
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();
        }

        private void PrintChannelBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"CHANNEL BLOCKS({shaderFile.TextureChannelProcessors.Count})");
            if (shaderFile.TextureChannelProcessors.Count > 0)
            {
                output.DefineHeaders(["index", "name", nameof(VfxTextureChannelProcessor.Channel), "inputs", nameof(VfxTextureChannelProcessor.ColorMode)]);
            }
            else
            {
                output.DefineHeaders([]);
                output.WriteLine("[none defined]");
            }
            foreach (var channelBlock in shaderFile.TextureChannelProcessors)
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

        private void PrintBufferBlocks(ShaderFile shaderFile)
        {
            if (shaderFile.ExtConstantBufferDescriptions.Count == 0)
            {
                output.WriteLine("BUFFER-BLOCKS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var bufferBlock in shaderFile.ExtConstantBufferDescriptions)
            {
                output.WriteLine($"BUFFER-BLOCK[{bufferBlock.BlockIndex}]");
                output.WriteLine($"{bufferBlock.Name} size={bufferBlock.BufferSize} param-count={bufferBlock.ParamCount}" +
                    $" arg0={bufferBlock.Arg0} crc32={bufferBlock.BlockCrc:x08}");
                output.DefineHeaders(["       ", "name", "offset", "vertex-size", "attrib-count", "data-count"]);
                foreach (var bufferParams in bufferBlock.BufferParams)
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

        private void PrintVertexSymbolBuffers(ShaderFile shaderFile)
        {
            output.WriteLine($"VERTEX-BUFFER-SYMBOLS({shaderFile.VSInputSignatures.Count})");
            if (shaderFile.VSInputSignatures.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            // find best padding
            var namePad = 0;
            var typePad = 0;
            var optionPad = 0;
            foreach (var symbolBlock in shaderFile.VSInputSignatures)
            {
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    namePad = Math.Max(namePad, symbolsDef.Name.Length);
                    typePad = Math.Max(namePad, symbolsDef.Type.Length);
                    optionPad = Math.Max(namePad, symbolsDef.Option.Length);
                }
            }
            foreach (var symbolBlock in shaderFile.VSInputSignatures)
            {
                output.WriteLine($"VERTEX-SYMBOLS[{symbolBlock.BlockIndex}] definitions={symbolBlock.SymbolsCount}");
                output.DefineHeaders(["       ",
                    "name".PadRight(namePad),
                    "type".PadRight(typePad),
                    $"option".PadRight(optionPad),
                    "semantic-index"]);
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    output.AddTabulatedRow(["",
                        $"{symbolsDef.Name}",
                        $"{symbolsDef.Type}",
                        $"{symbolsDef.Option}",
                        $"{symbolsDef.SemanticIndex,2}"]);
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintZFrames(ShaderFile shaderFile)
        {
            var zframesHeader = $"ZFRAMES({shaderFile.GetZFrameCount()})";
            output.WriteLine(zframesHeader);
            if (shaderFile.GetZFrameCount() == 0)
            {
                var infoText = "";
                if (shaderFile.VcsProgramType == VcsProgramType.Features)
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
            ConfigMappingSParams configGen = new(shaderFile);
            output.WriteLine(new string('-', zframesHeader.Length));
            // collect names in the order they appear
            List<string> sfNames = [];
            List<string> abbreviations = [];
            foreach (var sfBlock in shaderFile.StaticCombos)
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
            foreach (var zframeDesc in shaderFile.ZframesLookup)
            {
                if (zframeCount % 100 == 0 && configHeader.Trim().Length > 0)
                {
                    output.WriteLine($"{configHeader}");
                }
                var configState = configGen.GetConfigState(zframeDesc.Key);
                if (showRichTextBoxLinks)
                {
                    // the two backslashes registers the text as a link when viewed in a RichTextBox
                    output.WriteLine($"  Z[\\\\{zframeDesc.Key:x08}] {CombineIntsSpaceSep(configState, 6)}");
                }
                else
                {
                    output.WriteLine($"  Z[{zframeDesc.Key:x08}] {CombineIntsSpaceSep(configState, 6)}");
                }
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

        private static string Pow2Rep(int val)
        {
            var orig = val;
            var pow = 0;
            while (val > 1 && (val & 1) == 0)
            {
                val >>= 1;
                pow++;
            }
            if (val != 1)
            {
                return "" + orig;
            }
            return $"2^{pow}";
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

using System;
using System.IO;
using System.Collections.Generic;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;

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
            PrintMipmapBlocks(shaderFile);
            PrintBufferBlocks(shaderFile);
            PrintVertexSymbolBuffers(shaderFile);
            PrintZFrames(shaderFile);
        }

        private void PrintFeaturesHeader(ShaderFile shaderFile)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.FeaturesHeader.VcsFileVersion}");
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
                    output.Write($" \\\\{relatedFile.Replace("/", "\\")}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine($"VFX File Desc: {shaderFile.FeaturesHeader.FileDescription}");
            output.BreakLine();
            output.WriteLine($"has_psrs_file = {shaderFile.FeaturesHeader.HasPsrsFile}");
            output.WriteLine($"{nameof(shaderFile.FeaturesHeader.Version)} = {shaderFile.FeaturesHeader.Version}");
            var ftHeader = shaderFile.FeaturesHeader;
            output.WriteLine($"{nameof(ftHeader.DevShader)} = {ftHeader.DevShader}");
            output.WriteLine($"bool flags = ({ftHeader.Arg1},{ftHeader.Arg2},{ftHeader.Arg3}," +
                $"{ftHeader.Arg4},{ftHeader.Arg5},{ftHeader.Arg6},{ftHeader.Arg7}) (related to editor dependencies)");
            output.WriteLine($"possible editor description = {shaderFile.PossibleEditorDescription}");
            output.BreakLine();
            output.WriteLine("Editor/Shader compiler stack");
            for (var i = 0; i < ftHeader.EditorIDs.Count - 1; i++)
            {
                output.WriteLine($"{ftHeader.EditorIDs[i].Item1}    {ftHeader.EditorIDs[i].Item2}");
            }
            output.WriteLine($"{ftHeader.EditorIDs[^1].Item1}    // Editor ref. ID{ftHeader.EditorIDs.Count - 1} " +
                $"- common editor reference shared by multiple files");
            output.BreakLine();
            if (ftHeader.MainParams.Count == 0)
            {
                output.WriteLine("Primary modes");
                output.WriteLine("[default only]");
                return;
            }
            if (ftHeader.MainParams.Count > 1)
            {
                output.WriteLine($"Primary static modes (one of these should be selected)");
            }
            else
            {
                output.WriteLine($"Primary static modes (this file has only one default mode)");
            }
            output.DefineHeaders(new string[] { "name", "mode", "config-states" });
            output.AddTabulatedRow(new string[] { "----", "----", "-------------" });
            foreach (var mainParam in ftHeader.MainParams)
            {
                var arg2 = mainParam.Item2.Length == 0 ? "(default)" : mainParam.Item2;
                var configs = mainParam.Item2.Length == 0 ? "(implicit)" : "0,1";
                output.AddTabulatedRow(new string[] { $"{mainParam.Item1}", $"{arg2}", $"{configs}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintPsVsHeader(ShaderFile shaderFile)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.VspsHeader.VcsFileVersion}");
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
                    output.Write($" \\\\{relatedFile.Replace("/", "\\")}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine("Editor/Shader compiler stack");
            output.WriteLine($"{shaderFile.VspsHeader.FileID0}    // Editor ref. ID0 (produces this file)");
            output.WriteLine($"{shaderFile.VspsHeader.FileID1}    // Editor ref. ID1 " +
                $"- common editor reference shared by multiple files");
            output.WriteLine($"possible editor description = {shaderFile.PossibleEditorDescription}");
            output.BreakLine();
        }

        private void PrintFBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"FEATURE/STATIC-CONFIGURATIONS({shaderFile.SfBlocks.Count})");
            if (shaderFile.SfBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(new string[] { "index", "name", "nr-configs", "config-states", "" });
            foreach (var item in shaderFile.SfBlocks)
            {
                var configStates = "_";
                if (item.Arg2 > 0)
                {
                    configStates = "0";
                }
                for (var i = 1; i <= item.Arg2; i++)
                {
                    configStates += $",{i}";
                }
                var configStates2 = "";
                if (item.Arg2 > 1)
                {
                    configStates2 = $"{CombineStringArray(item.AdditionalParams.ToArray())}";
                }

                output.AddTabulatedRow(new string[] {$"[{item.BlockIndex,2}]", $"{item.Name0}", $"{item.Arg2+1}",
                    $"{configStates}", $"{configStates2}"});
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintSBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"STATIC-CONFIGURATIONS({shaderFile.SfBlocks.Count})");
            if (shaderFile.SfBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(new string[] { "index", "name", "arg2", "arg3", "arg4" });
            foreach (var item in shaderFile.SfBlocks)
            {
                output.AddTabulatedRow(new string[] { $"[{item.BlockIndex,2}]", $"{item.Name0}", $"{item.Arg2}", $"{item.Arg3}", $"{item.Arg4,2}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintStaticConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("STATIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.SfConstraintsBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var sfRuleBlock in shaderFile.SfConstraintsBlocks)
            {
                var sfNames = new string[sfRuleBlock.Range0.Length];
                for (var i = 0; i < sfNames.Length; i++)
                {
                    sfNames[i] = shaderFile.SfBlocks[sfRuleBlock.Range0[i]].Name0;
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(sfNames, BL);
                var s0 = $"[{sfRuleBlock.BlockIndex,2}]";
                var s1 = (sfRuleBlock.RelRule == 1 || sfRuleBlock.RelRule == 2) ? $"INC({sfRuleBlock.RelRule})" : $"EXC({sfRuleBlock.RelRule})";
                var s3 = $"{sfRuleBlock.GetByteFlagsAsString()}";
                var s4 = $"{breakNames[0]}";
                var s5 = $"{CombineIntArray(sfRuleBlock.Range0)}";
                var s6 = $"{CombineIntArray(sfRuleBlock.Range1)}";
                var s7 = $"{CombineIntArray(sfRuleBlock.Range2)}";
                var blockSummary = $"{s0,-7}{s1,-10}{s5,-16}{s4,-BL}{s6,-8}{s7,-8}";
                for (var i = 1; i < breakNames.Length; i++)
                {
                    blockSummary += $"\n{"",7}{"",10}{"",16}{breakNames[i],-BL}";
                }
                output.Write(blockSummary);
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintDynamicConfigurations(ShaderFile shaderFile)
        {
            output.WriteLine($"DYNAMIC-CONFIGURATIONS({shaderFile.DBlocks.Count})");
            if (shaderFile.DBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            int[] pad = { 7, 40, 7, 7, 7 };
            var h0 = "index";
            var h1 = "name";
            var h2 = "arg2";
            var h3 = "arg3";
            var h4 = "arg4";
            var blockHeader = $"{h0.PadRight(pad[0])} {h1.PadRight(pad[1])} {h2.PadRight(pad[2])} {h3.PadRight(pad[3])} {h4.PadRight(pad[4])}";
            output.WriteLine(blockHeader);
            foreach (var dBlock in shaderFile.DBlocks)
            {
                var v0 = $"[{dBlock.BlockIndex,2}]";
                var v1 = dBlock.Name0;
                var v2 = "" + dBlock.Arg2;
                var v3 = "" + dBlock.Arg3;
                var v4 = $"{dBlock.Arg4,2}";
                var blockSummary = $"{v0.PadRight(pad[0])} {v1.PadRight(pad[1])} {v2.PadRight(pad[2])} {v3.PadRight(pad[3])} {v4.PadRight(pad[4])}";
                output.WriteLine(blockSummary);
            }
            if (shaderFile.DBlocks.Count == 0)
            {
                output.WriteLine("[empty list]");
            }
            output.BreakLine();
        }

        private void PrintDynamicConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("DYNAMIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.DConstraintsBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var dRuleBlock in shaderFile.DConstraintsBlocks)
            {
                var dRuleName = new string[dRuleBlock.Flags.Length];
                for (var i = 0; i < dRuleName.Length; i++)
                {
                    if (dRuleBlock.Flags[i] == 3)
                    {
                        dRuleName[i] = shaderFile.DBlocks[dRuleBlock.Range0[i]].Name0;
                        continue;
                    }
                    if (dRuleBlock.Flags[i] == 2)
                    {
                        dRuleName[i] = shaderFile.SfBlocks[dRuleBlock.Range0[i]].Name0;
                        continue;
                    }
                    throw new ShaderParserException($"unknown flag value {dRuleBlock.Flags[i]}");
                }
                const int BL = 70;
                var breakNames = CombineValuesBreakString(dRuleName, BL);
                var s0 = $"[{dRuleBlock.BlockIndex,2}]";
                var s1 = (dRuleBlock.RelRule == 1 || dRuleBlock.RelRule == 2) ? $"INC({dRuleBlock.RelRule})" : $"EXC({dRuleBlock.RelRule})";
                var s3 = $"{dRuleBlock.ReadByteFlagsAsString()}";
                var s4 = $"{breakNames[0]}";
                var s5 = $"{CombineIntArray(dRuleBlock.Range0)}";
                var s6 = $"{CombineIntArray(dRuleBlock.Range1)}";
                var s7 = $"{CombineIntArray(dRuleBlock.Range2)}";
                var blockSummary = $"{s0,-7}{s1,-10}{s3,-15}{s5,-16}{s4,-BL}{s6,-10}{s7,-8}";
                for (var i = 1; i < breakNames.Length; i++)
                {
                    blockSummary += $"\n{"",-7}{"",-10}{"",-15}{"",-16}{breakNames[i],-BL}";
                }
                output.Write(blockSummary);
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintParameters(ShaderFile shaderFile)
        {
            if (shaderFile.ParamBlocks.Count == 0)
            {
                output.WriteLine($"PARAMETERS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            var dynExpCount = 0;
            var indexPad = shaderFile.ParamBlocks.Count > 100 ? 3 : 2;
            // parameters
            output.WriteLine($"PARAMETERS({shaderFile.ParamBlocks.Count})    *dyn-expressions shown separately");
            output.DefineHeaders(new string[] { "index", "name0 | name1 | name2", "type0", "type1", "res", "arg0", "arg1",
                "arg2", "arg3", "arg4", "arg5", "dyn-exp*", "command 0|1", "fileref"});
            foreach (var param in shaderFile.ParamBlocks)
            {
                var nameCondensed = param.Name0;
                if (param.Name1.Length > 0)
                {
                    nameCondensed += $" | {param.Name1}";
                }
                if (param.Name2.Length > 0)
                {
                    nameCondensed += $" | {param.Name2}(2)";
                }
                if (nameCondensed.Length > 65)
                {
                    var tokens = nameCondensed.Split("|");
                    nameCondensed = tokens[0].Trim();
                    for (var i = 1; i < tokens.Length; i++)
                    {
                        nameCondensed += $"\n{tokens[i].Trim()}";
                    }
                }
                var dynExpExists = param.Lead0 == 6 || param.Lead0 == 7 ? "true" : "";
                if (dynExpExists.Length > 0)
                {
                    dynExpCount++;
                }
                var c0 = param.Command0;
                var c1 = param.Command1;
                if (c1.Length > 0)
                {
                    c0 += $" | {c1}";
                }
                output.AddTabulatedRow(new string[] {$"[{(""+param.BlockIndex).PadLeft(indexPad)}]", $"{nameCondensed}", $"{param.Type}",
                    $"{param.Lead0}", $"{param.Res0}", $"{BlankNegOne(param.Arg0),2}", $"{param.Arg1,2}", $"{param.Arg2}",
                    $"{Pow2Rep(param.Arg3),4}", $"{param.Arg4,2}", $"{BlankNegOne(param.Arg5),2}",
                    $"{dynExpExists}", $"{c0}", $"{param.FileRef}"});
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
                output.DefineHeaders(new string[] { "param-index", "name0", "t0,t1,a0,a1,a2,a4,a5  ", "dyn-exp" });
                foreach (var param in shaderFile.ParamBlocks)
                {
                    if (param.Lead0 != 6 && param.Lead0 != 7)
                    {
                        continue;
                    }
                    var dynExpstring = ParseDynamicExpression(param.DynExp);
                    output.AddTabulatedRow(new string[] { $"[{(""+param.BlockIndex).PadLeft(indexPad)}]",
                        $"{param.Name0}",
                        $"{param.Type,2},{param.Lead0,2},{BlankNegOne(param.Arg0),2},{param.Arg1,2},{param.Arg2,2},{param.Arg4,2},{BlankNegOne(param.Arg5),2}",
                        $"{dynExpstring}" });
                }
                output.PrintTabulatedValues();
            }
            output.BreakLine();
            output.WriteLine("PARAMETERS - Default values and limits    (type0,type1,arg0,arg1,arg2,arg4,arg5,command0 reprinted)");
            output.WriteLine("(- indicates -infinity, + indicates +infinity, def. = default)");
            output.DefineHeaders(new string[] { "index", "name0", "t0,t1,a0,a1,a2,a4,a5  ", "ints-def.", "ints-min", "ints-max",
                "floats-def.", "floats-min", "floats-max", "int-args0", "int-args1", "command0", "fileref", "dyn-exp"});
            foreach (var param in shaderFile.ParamBlocks)
            {
                var fileref = param.FileRef;
                var r0 = param.Ranges0;
                var r1 = param.Ranges1;
                var r2 = param.Ranges2;
                var r3 = param.Ranges3;
                var r4 = param.Ranges4;
                var r5 = param.Ranges5;
                var r6 = param.Ranges6;
                var r7 = param.Ranges7;
                var hasFileRef = param.FileRef.Length > 0 ? "true" : "";
                var hasDynExp = param.Lead0 == 6 || param.Lead0 == 7 ? "true" : "";
                output.AddTabulatedRow(new string[] { $"[{("" + param.BlockIndex).PadLeft(indexPad)}]", $"{param.Name0}",
                    $"{param.Type,2},{param.Lead0,2},{BlankNegOne(param.Arg0),2},{param.Arg1,2},{param.Arg2,2},{param.Arg4,2},{BlankNegOne(param.Arg5),2}",
                    $"{Comb(r0)}", $"{Comb(r1)}", $"{Comb(r2)}", $"{Comb(r3)}", $"{Comb(r4)}",
                    $"{Comb(r5)}", $"{Comb(r6)}", $"{Comb(r7)}", $"{param.Command0}", $"{hasFileRef}", $"{hasDynExp}"});
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();
        }

        private void PrintMipmapBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"MIPMAP BLOCKS({shaderFile.MipmapBlocks.Count})");
            if (shaderFile.MipmapBlocks.Count > 0)
            {
                output.DefineHeaders(new string[] { "index", "name", "arg0", "arg1", "arg2", "arg3", "arg4", "arg5" });
            }
            else
            {
                output.DefineHeaders(Array.Empty<string>());
                output.WriteLine("[none defined]");
            }
            foreach (var mipmap in shaderFile.MipmapBlocks)
            {
                output.AddTabulatedRow(new string[] { $"[{mipmap.BlockIndex,2}]", $"{mipmap.Name}",
                    $"{BytesToString(mipmap.Arg0),-14}", $"{mipmap.Arg1,2}", $"{BlankNegOne(mipmap.Arg2),2}",
                    $"{BlankNegOne(mipmap.Arg3),2}", $"{BlankNegOne(mipmap.Arg4),2}", $"{mipmap.Arg5,2}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintBufferBlocks(ShaderFile shaderFile)
        {
            if (shaderFile.BufferBlocks.Count == 0)
            {
                output.WriteLine("BUFFER-BLOCKS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var bufferBlock in shaderFile.BufferBlocks)
            {
                output.WriteLine($"BUFFER-BLOCK[{bufferBlock.BlockIndex}]");
                output.WriteLine($"{bufferBlock.Name} size={bufferBlock.BufferSize} param-count={bufferBlock.ParamCount}" +
                    $" arg0={bufferBlock.Arg0} crc32={bufferBlock.BlockCrc:x08}");
                output.DefineHeaders(new string[] { "       ", "name", "offset", "vertex-size", "attrib-count", "data-count" });
                foreach (var bufferParams in bufferBlock.BufferParams)
                {
                    var name = bufferParams.Item1;
                    var bOffset = bufferParams.Item2;
                    var nrVertices = bufferParams.Item3;
                    var nrAttribs = bufferParams.Item4;
                    var length = bufferParams.Item5;
                    output.AddTabulatedRow(new string[] { "", $"{name}", $"{bOffset,3}", $"{nrVertices,3}", $"{nrAttribs,3}", $"{length,3}" });

                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
        }

        private void PrintVertexSymbolBuffers(ShaderFile shaderFile)
        {
            output.WriteLine($"VERTEX-BUFFER-SYMBOLS({shaderFile.SymbolBlocks.Count})");
            if (shaderFile.SymbolBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            // find best padding
            var namePad = 0;
            var typePad = 0;
            var optionPad = 0;
            foreach (var symbolBlock in shaderFile.SymbolBlocks)
            {
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    namePad = Math.Max(namePad, symbolsDef.Item1.Length);
                    typePad = Math.Max(namePad, symbolsDef.Item2.Length);
                    optionPad = Math.Max(namePad, symbolsDef.Item3.Length);
                }
            }
            foreach (var symbolBlock in shaderFile.SymbolBlocks)
            {
                output.WriteLine($"VERTEX-SYMBOLS[{symbolBlock.BlockIndex}] definitions={symbolBlock.SymbolsCount}");
                output.DefineHeaders(new string[] { "       ", "name".PadRight(namePad), "type".PadRight(typePad),
                    $"option".PadRight(optionPad), "semantic-index" });
                foreach (var symbolsDef in symbolBlock.SymbolsDefinition)
                {
                    var name = symbolsDef.Item1;
                    var type = symbolsDef.Item2;
                    var option = symbolsDef.Item3;
                    var semanticIndex = symbolsDef.Item4;
                    output.AddTabulatedRow(new string[] { "", $"{name}", $"{type}", $"{option}", $"{semanticIndex,2}" });
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
            List<string> sfNames = new();
            List<string> abbreviations = new();
            foreach (var sfBlock in shaderFile.SfBlocks)
            {
                var sfShortName = ShortenShaderParam(sfBlock.Name0).ToLower();
                abbreviations.Add($"{sfBlock.Name0}({sfShortName})");
                sfNames.Add(sfShortName);
            }
            var breakabbreviations = CombineValuesBreakString(abbreviations.ToArray(), 120);
            foreach (var abbr in breakabbreviations)
            {
                output.WriteLine(abbr);
            }
            if (abbreviations.Count > 0)
            {
                output.BreakLine();
            }

            var configHeader = CombineStringsSpaceSep(sfNames.ToArray(), 6);
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

            return "" + val; ;
        }
    }

}

using System;
using System.IO;
using System.Collections.Generic;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;
using static ValveResourceFormat.CompiledShader.ShaderDataReader;

namespace ValveResourceFormat.CompiledShader
{
    public class PrintVcsFileSummary
    {
        private OutputFormatterTabulatedData output;
        private bool showRichTextBoxLinks;
        private List<string> relatedFiles;

        public PrintVcsFileSummary(ShaderFile shaderFile, HandleOutputWrite OutputWriter = null,
            bool showRichTextBoxLinks = false, List<string> relatedFiles = null)
        {
            this.showRichTextBoxLinks = showRichTextBoxLinks;
            this.relatedFiles = relatedFiles;

            output = new OutputFormatterTabulatedData(OutputWriter);
            if (shaderFile.vcsProgramType == VcsProgramType.Features)
            {
                PrintFeaturesHeader(shaderFile);
                PrintFBlocks(shaderFile);
            } else
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
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.featuresHeader.vcsFileVersion}");
            output.BreakLine();
            output.Write($"Showing {shaderFile.vcsProgramType}: {Path.GetFileName(shaderFile.filenamepath)}");
            if (showRichTextBoxLinks)
            {
                output.WriteLine($" (view byte detail \\\\{Path.GetFileName(shaderFile.filenamepath)}\\bytes)");
            } else
            {
                output.BreakLine();
            }
            if (showRichTextBoxLinks && relatedFiles != null && relatedFiles.Count > 1)
            {
                output.Write("Related files:");
                foreach (var relatedFile in relatedFiles)
                {
                    output.Write($" \\\\{relatedFile.Replace("/","\\")}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine($"VFX File Desc: {shaderFile.featuresHeader.file_description}");
            output.BreakLine();
            output.WriteLine($"has_psrs_file = {shaderFile.featuresHeader.has_psrs_file}");
            output.WriteLine($"{nameof(shaderFile.featuresHeader.Version)} = {shaderFile.featuresHeader.Version}");
            var ftHeader = shaderFile.featuresHeader;
            output.WriteLine($"{nameof(ftHeader.DevShader)} = {ftHeader.DevShader}");
            output.WriteLine($"bool flags = ({ftHeader.arg1},{ftHeader.arg2},{ftHeader.arg3}," +
                $"{ftHeader.arg4},{ftHeader.arg5},{ftHeader.arg6},{ftHeader.arg7}) (related to editor dependencies)");
            output.WriteLine($"possible editor description = {shaderFile.possibleEditorDescription}");
            output.BreakLine();
            output.WriteLine("Editor/Shader compiler stack");
            for (int i = 0; i < ftHeader.editorIDs.Count - 1; i++)
            {
                output.WriteLine($"{ftHeader.editorIDs[i].Item1}    {ftHeader.editorIDs[i].Item2}");
            }
            output.WriteLine($"{ftHeader.editorIDs[^1].Item1}    // Editor ref. ID{ftHeader.editorIDs.Count - 1} " +
                $"- common editor reference shared by multiple files");
            output.BreakLine();
            if (ftHeader.mainParams.Count == 0)
            {
                output.WriteLine("Primary modes");
                output.WriteLine("[default only]");
                return;
            }
            if (ftHeader.mainParams.Count > 1)
            {
                output.WriteLine($"Primary static modes (one of these should be selected)");
            } else
            {
                output.WriteLine($"Primary static modes (this file has only one default mode)");
            }
            output.DefineHeaders(new string[] { "name", "mode", "config-states" });
            output.AddTabulatedRow(new string[] { "----", "----", "-------------" });
            foreach (var mainParam in ftHeader.mainParams)
            {
                string arg2 = mainParam.Item2.Length == 0 ? "(default)" : mainParam.Item2;
                string configs = mainParam.Item2.Length == 0 ? "(implicit)" : "0,1";
                output.AddTabulatedRow(new string[] { $"{mainParam.Item1}", $"{arg2}", $"{configs}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintPsVsHeader(ShaderFile shaderFile)
        {
            output.WriteLine($"Valve Compiled Shader 2 (vcs2), version {shaderFile.vspsHeader.vcsFileVersion}");
            output.BreakLine();
            output.Write($"Showing {shaderFile.vcsProgramType}: {Path.GetFileName(shaderFile.filenamepath)}");
            if (showRichTextBoxLinks)
            {
                output.WriteLine($" (view byte detail \\\\{Path.GetFileName(shaderFile.filenamepath)}\\bytes)");
            } else
            {
                output.BreakLine();
            }
            if (showRichTextBoxLinks && relatedFiles != null && relatedFiles.Count > 1)
            {
                output.Write("Related files:");
                foreach (var relatedFile in relatedFiles)
                {
                    output.Write($" \\\\{relatedFile.Replace("/","\\")}");
                }
                output.BreakLine();
            }
            output.BreakLine();

            output.WriteLine("Editor/Shader compiler stack");
            output.WriteLine($"{shaderFile.vspsHeader.fileID0}    // Editor ref. ID0 (produces this file)");
            output.WriteLine($"{shaderFile.vspsHeader.fileID1}    // Editor ref. ID1 " +
                $"- common editor reference shared by multiple files");
            output.WriteLine($"possible editor description = {shaderFile.possibleEditorDescription}");
            output.BreakLine();
        }

        private void PrintFBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"FEATURE/STATIC-CONFIGURATIONS({shaderFile.sfBlocks.Count})");
            if (shaderFile.sfBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(new string[] { "index", "name", "nr-configs", "config-states", "" });
            foreach (var item in shaderFile.sfBlocks)
            {
                string configStates = "_";
                if (item.arg2 > 0)
                {
                    configStates = "0";
                }
                for (int i = 1; i <= item.arg2; i++)
                {
                    configStates += $",{i}";
                }
                string configStates2 = "";
                if (item.arg2 > 1)
                {
                    configStates2 = $"{CombineStringArray(item.additionalParams.ToArray())}";
                }

                output.AddTabulatedRow(new string[] {$"[{item.blockIndex,2}]", $"{item.name0}", $"{item.arg2+1}",
                    $"{configStates}", $"{configStates2}"});
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintSBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"STATIC-CONFIGURATIONS({shaderFile.sfBlocks.Count})");
            if (shaderFile.sfBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            output.DefineHeaders(new string[] { "index", "name", "arg2", "arg3", "arg4" });
            foreach (var item in shaderFile.sfBlocks)
            {
                output.AddTabulatedRow(new string[] { $"[{item.blockIndex,2}]", $"{item.name0}", $"{item.arg2}", $"{item.arg3}", $"{item.arg4,2}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintStaticConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("STATIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.sfConstraintsBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (SfConstraintsBlock sfRuleBlock in shaderFile.sfConstraintsBlocks)
            {
                string[] sfNames = new string[sfRuleBlock.range0.Length];
                for (int i = 0; i < sfNames.Length; i++)
                {
                    sfNames[i] = shaderFile.sfBlocks[sfRuleBlock.range0[i]].name0;
                }
                const int BL = 70;
                string[] breakNames = CombineValuesBreakString(sfNames, BL);
                string s0 = $"[{sfRuleBlock.blockIndex,2}]";
                string s1 = (sfRuleBlock.relRule == 1 || sfRuleBlock.relRule == 2) ? $"INC({sfRuleBlock.relRule})" : $"EXC({sfRuleBlock.relRule})";
                string s3 = $"{sfRuleBlock.GetByteFlagsAsString()}";
                string s4 = $"{breakNames[0]}";
                string s5 = $"{CombineIntArray(sfRuleBlock.range0)}";
                string s6 = $"{CombineIntArray(sfRuleBlock.range1)}";
                string s7 = $"{CombineIntArray(sfRuleBlock.range2)}";
                string blockSummary = $"{s0.PadRight(7)}{s1.PadRight(10)}{s5.PadRight(16)}{s4.PadRight(BL)}{s6.PadRight(8)}{s7.PadRight(8)}";
                for (int i = 1; i < breakNames.Length; i++)
                {
                    blockSummary += $"\n{(""),7}{(""),10}{(""),16}{breakNames[i],-BL}";
                }
                output.Write(blockSummary);
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintDynamicConfigurations(ShaderFile shaderFile)
        {
            output.WriteLine($"DYNAMIC-CONFIGURATIONS({shaderFile.dBlocks.Count})");
            if (shaderFile.dBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            int[] pad = { 7, 40, 7, 7, 7 };
            string h0 = "index";
            string h1 = "name";
            string h2 = "arg2";
            string h3 = "arg3";
            string h4 = "arg4";
            string blockHeader = $"{h0.PadRight(pad[0])} {h1.PadRight(pad[1])} {h2.PadRight(pad[2])} {h3.PadRight(pad[3])} {h4.PadRight(pad[4])}";
            output.WriteLine(blockHeader);
            foreach (DBlock dBlock in shaderFile.dBlocks)
            {
                string v0 = $"[{dBlock.blockIndex,2}]";
                string v1 = dBlock.name0;
                string v2 = "" + dBlock.arg2;
                string v3 = "" + dBlock.arg3;
                string v4 = $"{dBlock.arg4,2}";
                string blockSummary = $"{v0.PadRight(pad[0])} {v1.PadRight(pad[1])} {v2.PadRight(pad[2])} {v3.PadRight(pad[3])} {v4.PadRight(pad[4])}";
                output.WriteLine(blockSummary);
            }
            if (shaderFile.dBlocks.Count == 0)
            {
                output.WriteLine("[empty list]");
            }
            output.BreakLine();
        }

        private void PrintDynamicConstraints(ShaderFile shaderFile)
        {
            output.WriteLine("DYNAMIC-CONFIGS INCLUSION/EXCLUSION RULES");
            if (shaderFile.dConstraintsBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (DConstraintsBlock dRuleBlock in shaderFile.dConstraintsBlocks)
            {
                string[] dRuleName = new string[dRuleBlock.flags.Length];
                for (int i = 0; i < dRuleName.Length; i++)
                {
                    if (dRuleBlock.flags[i] == 3)
                    {
                        dRuleName[i] = shaderFile.dBlocks[dRuleBlock.range0[i]].name0;
                        continue;
                    }
                    if (dRuleBlock.flags[i] == 2)
                    {
                        dRuleName[i] = shaderFile.sfBlocks[dRuleBlock.range0[i]].name0;
                        continue;
                    }
                    throw new ShaderParserException($"unknown flag value {dRuleBlock.flags[i]}");
                }
                const int BL = 70;
                string[] breakNames = CombineValuesBreakString(dRuleName, BL);
                string s0 = $"[{dRuleBlock.blockIndex,2}]";
                string s1 = (dRuleBlock.relRule == 1 || dRuleBlock.relRule == 2) ? $"INC({dRuleBlock.relRule})" : $"EXC({dRuleBlock.relRule})";
                string s3 = $"{dRuleBlock.ReadByteFlagsAsString()}";
                string s4 = $"{breakNames[0]}";
                string s5 = $"{CombineIntArray(dRuleBlock.range0)}";
                string s6 = $"{CombineIntArray(dRuleBlock.range1)}";
                string s7 = $"{CombineIntArray(dRuleBlock.range2)}";
                string blockSummary = $"{s0,-7}{s1,-10}{s3,-15}{s5,-16}{s4,-BL}{s6,-10}{s7,-8}";
                for (int i = 1; i < breakNames.Length; i++)
                {
                    blockSummary += $"\n{(""),-7}{(""),-10}{(""),-15}{(""),-16}{breakNames[i],-BL}";
                }
                output.Write(blockSummary);
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintParameters(ShaderFile shaderFile)
        {
            if (shaderFile.paramBlocks.Count == 0)
            {
                output.WriteLine($"PARAMETERS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            int dynExpCount = 0;
            int indexPad = shaderFile.paramBlocks.Count > 100 ? 3 : 2;
            // parameters
            output.WriteLine($"PARAMETERS({shaderFile.paramBlocks.Count})    *dyn-expressions shown separately");
            output.DefineHeaders(new string[] { "index", "name0 | name1 | name2", "type0", "type1", "res", "arg0", "arg1",
                "arg2", "arg3", "arg4", "arg5", "dyn-exp*", "command 0|1", "fileref"});
            foreach (var param in shaderFile.paramBlocks)
            {
                string nameCondensed = param.name0;
                if (param.name1.Length > 0)
                {
                    nameCondensed += $" | {param.name1}";
                }
                if (param.name2.Length > 0)
                {
                    nameCondensed += $" | {param.name2}(2)";
                }
                if (nameCondensed.Length > 65)
                {
                    string[] tokens = nameCondensed.Split("|");
                    nameCondensed = tokens[0].Trim();
                    for (int i = 1; i < tokens.Length; i++)
                    {
                        nameCondensed += $"\n{tokens[i].Trim()}";
                    }
                }
                string dynExpExists = param.lead0 == 6 || param.lead0 == 7 ? "true" : "";
                if (dynExpExists.Length > 0)
                {
                    dynExpCount++;
                }
                string c0 = param.command0;
                string c1 = param.command1;
                if (c1.Length > 0)
                {
                    c0 += $" | {c1}";
                }
                output.AddTabulatedRow(new string[] {$"[{(""+param.blockIndex).PadLeft(indexPad)}]", $"{nameCondensed}", $"{param.type}",
                    $"{param.lead0}", $"{param.res0}", $"{BlankNegOne(param.arg0),2}", $"{param.arg1,2}", $"{param.arg2}",
                    $"{Pow2Rep(param.arg3),4}", $"{param.arg4,2}", $"{BlankNegOne(param.arg5),2}",
                    $"{dynExpExists}", $"{c0}", $"{param.fileref}"});
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();
            if (dynExpCount == 0)
            {
                output.WriteLine($"DYNAMIC EXPRESSIONS({dynExpCount})");
                output.WriteLine("[none defined]");
            } else
            {
                output.WriteLine($"DYNAMIC EXPRESSIONS({dynExpCount})    (name0,type0,type1,arg0,arg1,arg2,arg4,arg5 reprinted)");
                output.DefineHeaders(new string[] { "param-index", "name0", "t0,t1,a0,a1,a2,a4,a5  ", "dyn-exp" });
                foreach (var param in shaderFile.paramBlocks)
                {
                    if (param.lead0 != 6 && param.lead0 != 7)
                    {
                        continue;
                    }
                    string dynExpstring = ParseDynamicExpression(param.dynExp);
                    output.AddTabulatedRow(new string[] { $"[{(""+param.blockIndex).PadLeft(indexPad)}]",
                        $"{param.name0}",
                        $"{param.type,2},{param.lead0,2},{BlankNegOne(param.arg0),2},{param.arg1,2},{param.arg2,2},{param.arg4,2},{BlankNegOne(param.arg5),2}",
                        $"{dynExpstring}" });
                }
                output.PrintTabulatedValues();
            }
            output.BreakLine();
            output.WriteLine("PARAMETERS - Default values and limits    (type0,type1,arg0,arg1,arg2,arg4,arg5,command0 reprinted)");
            output.WriteLine("(- indicates -infinity, + indicates +infinity, def. = default)");
            output.DefineHeaders(new string[] { "index", "name0", "t0,t1,a0,a1,a2,a4,a5  ", "ints-def.", "ints-min", "ints-max",
                "floats-def.", "floats-min", "floats-max", "int-args0", "int-args1", "command0", "fileref", "dyn-exp"});
            foreach (var param in shaderFile.paramBlocks)
            {
                string fileref = param.fileref;
                int[] r0 = param.ranges0;
                int[] r1 = param.ranges1;
                int[] r2 = param.ranges2;
                float[] r3 = param.ranges3;
                float[] r4 = param.ranges4;
                float[] r5 = param.ranges5;
                int[] r6 = param.ranges6;
                int[] r7 = param.ranges7;
                string hasFileRef = param.fileref.Length > 0 ? "true" : "";
                string hasDynExp = param.lead0 == 6 || param.lead0 == 7 ? "true" : "";
                output.AddTabulatedRow(new string[] { $"[{("" + param.blockIndex).PadLeft(indexPad)}]", $"{param.name0}",
                    $"{param.type,2},{param.lead0,2},{BlankNegOne(param.arg0),2},{param.arg1,2},{param.arg2,2},{param.arg4,2},{BlankNegOne(param.arg5),2}",
                    $"{Comb(r0)}", $"{Comb(r1)}", $"{Comb(r2)}", $"{Comb(r3)}", $"{Comb(r4)}",
                    $"{Comb(r5)}", $"{Comb(r6)}", $"{Comb(r7)}", $"{param.command0}", $"{hasFileRef}", $"{hasDynExp}"});
            }
            output.PrintTabulatedValues(spacing: 1);
            output.BreakLine();
        }

        private void PrintMipmapBlocks(ShaderFile shaderFile)
        {
            output.WriteLine($"MIPMAP BLOCKS({shaderFile.mipmapBlocks.Count})");
            if (shaderFile.mipmapBlocks.Count > 0)
            {
                output.DefineHeaders(new string[] { "index", "name", "arg0", "arg1", "arg2", "arg3", "arg4", "arg5" });
            } else
            {
                output.DefineHeaders(Array.Empty<string>());
                output.WriteLine("[none defined]");
            }
            foreach (var mipmap in shaderFile.mipmapBlocks)
            {
                output.AddTabulatedRow(new string[] { $"[{mipmap.blockIndex,2}]", $"{mipmap.name}",
                    $"{BytesToString(mipmap.arg0),-14}", $"{mipmap.arg1,2}", $"{BlankNegOne(mipmap.arg2),2}",
                    $"{BlankNegOne(mipmap.arg3),2}", $"{BlankNegOne(mipmap.arg4),2}", $"{mipmap.arg5,2}" });
            }
            output.PrintTabulatedValues();
            output.BreakLine();
        }

        private void PrintBufferBlocks(ShaderFile shaderFile)
        {
            if (shaderFile.bufferBlocks.Count == 0)
            {
                output.WriteLine("BUFFER-BLOCKS(0)");
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            foreach (var bufferBlock in shaderFile.bufferBlocks)
            {
                output.WriteLine($"BUFFER-BLOCK[{bufferBlock.blockIndex}]");
                output.WriteLine($"{bufferBlock.name} size={bufferBlock.bufferSize} param-count={bufferBlock.paramCount}" +
                    $" arg0={bufferBlock.arg0} crc32={bufferBlock.blockCrc:x08}");
                output.DefineHeaders(new string[] { "       ", "name", "offset", "vertex-size", "attrib-count", "data-count" });
                foreach (var bufferParams in bufferBlock.bufferParams)
                {
                    string name = bufferParams.Item1;
                    int bOffset = bufferParams.Item2;
                    int nrVertices = bufferParams.Item3;
                    int nrAttribs = bufferParams.Item4;
                    int length = bufferParams.Item5;
                    output.AddTabulatedRow(new string[] { "", $"{name}", $"{bOffset,3}", $"{nrVertices,3}", $"{nrAttribs,3}", $"{length,3}" });

                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
        }

        private void PrintVertexSymbolBuffers(ShaderFile shaderFile)
        {
            output.WriteLine($"VERTEX-BUFFER-SYMBOLS({shaderFile.symbolBlocks.Count})");
            if (shaderFile.symbolBlocks.Count == 0)
            {
                output.WriteLine("[none defined]");
                output.BreakLine();
                return;
            }
            // find best padding
            int namePad = 0;
            int typePad = 0;
            int optionPad = 0;
            foreach (var symbolBlock in shaderFile.symbolBlocks)
            {
                foreach (var symbolsDef in symbolBlock.symbolsDefinition)
                {
                    namePad = Math.Max(namePad, symbolsDef.Item1.Length);
                    typePad = Math.Max(namePad, symbolsDef.Item2.Length);
                    optionPad = Math.Max(namePad, symbolsDef.Item3.Length);
                }
            }
            foreach (var symbolBlock in shaderFile.symbolBlocks)
            {
                output.WriteLine($"VERTEX-SYMBOLS[{symbolBlock.blockIndex}] definitions={symbolBlock.symbolsCount}");
                output.DefineHeaders(new string[] { "       ", "name".PadRight(namePad), "type".PadRight(typePad),
                    $"option".PadRight(optionPad), "semantic-index" });
                foreach (var symbolsDef in symbolBlock.symbolsDefinition)
                {
                    string name = symbolsDef.Item1;
                    string type = symbolsDef.Item2;
                    string option = symbolsDef.Item3;
                    int semanticIndex = symbolsDef.Item4;
                    output.AddTabulatedRow(new string[] { "", $"{name}", $"{type}", $"{option}", $"{semanticIndex,2}" });
                }
                output.PrintTabulatedValues();
                output.BreakLine();
            }
            output.BreakLine();
        }

        private void PrintZFrames(ShaderFile shaderFile)
        {
            string zframesHeader = $"ZFRAMES({shaderFile.GetZFrameCount()})";
            output.WriteLine(zframesHeader);
            if (shaderFile.GetZFrameCount() == 0)
            {
                string infoText = "";
                if (shaderFile.vcsProgramType == VcsProgramType.Features)
                {
                    infoText = "(Features files in general don't contain zframes)";
                }
                output.WriteLine($"[none defined] {infoText}");
                output.BreakLine();
                return;
            }
            // print the config headers every 100 frames
            int zframeCount = 0;
            // prepare the lookup to determine configuration state
            ConfigMappingSParams configGen = new(shaderFile);
            output.WriteLine(new string('-', zframesHeader.Length));
            // collect names in the order they appear
            List<string> sfNames = new();
            List<string> abbreviations = new();
            foreach (var sfBlock in shaderFile.sfBlocks)
            {
                string sfShortName = ShortenShaderParam(sfBlock.name0).ToLower();
                abbreviations.Add($"{sfBlock.name0}({sfShortName})");
                sfNames.Add(sfShortName);
            }
            string[] breakabbreviations = CombineValuesBreakString(abbreviations.ToArray(), 120);
            foreach (string abbr in breakabbreviations)
            {
                output.WriteLine(abbr);
            }
            if (abbreviations.Count > 0)
            {
                output.BreakLine();
            }

            string configHeader = CombineStringsSpaceSep(sfNames.ToArray(), 6);
            configHeader = $"{new string(' ', 16)}{configHeader}";
            foreach (var zframeDesc in shaderFile.zframesLookup)
            {
                if (zframeCount % 100 == 0 && configHeader.Trim().Length > 0)
                {
                    output.WriteLine($"{configHeader}");
                }
                int[] configState = configGen.GetConfigState(zframeDesc.Key);
                if (showRichTextBoxLinks)
                {
                    // the two backslashes registers the text as a link when viewed in a RichTextBox
                    output.WriteLine($"  Z[\\\\{zframeDesc.Key:x08}] {CombineIntsSpaceSep(configState, 6)}");
                } else
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
            int orig = val;
            int pow = 0;
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
            if (val == -1e9) return "-";
            if (val == 1e9) return "+";
            return $"{val}";
        }

        private static string Fmt(int val)
        {
            if (val == -999999999) return "-";
            if (val == 999999999) return "+";
            return "" + val; ;
        }
    }

}

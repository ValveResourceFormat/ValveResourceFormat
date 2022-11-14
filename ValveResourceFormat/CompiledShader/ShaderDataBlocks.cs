using System;
using System.Collections.Generic;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class FeaturesHeaderBlock : ShaderDataBlock
    {
        public int vcsFileVersion { get; }
        public bool has_psrs_file { get; }
        public int Version { get; }
        public string file_description { get; }
        public int DevShader { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; }
        public int arg6 { get; }
        public int arg7 { get; } = -1;
        public List<(string, string)> mainParams { get; } = new();
        public List<(string, string)> editorIDs { get; } = new();
        public FeaturesHeaderBlock(ShaderDataReader datareader) : base(datareader)
        {
            var vcsMagicId = datareader.ReadInt32();
            if (vcsMagicId != ShaderFile.MAGIC)
            {
                throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                    vcsMagicId, nameof(vcsMagicId));
            }

            vcsFileVersion = datareader.ReadInt32();
            ThrowIfNotSupported(vcsFileVersion);

            var psrs_arg = 0;
            if (vcsFileVersion >= 64)
            {
                psrs_arg = datareader.ReadInt32();
            }

            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"unexpected value psrs_arg = {psrs_arg}");
            }
            has_psrs_file = psrs_arg > 0;
            Version = datareader.ReadInt32();
            datareader.ReadInt32(); // length of name, but not needed because it's always null-term
            file_description = datareader.ReadNullTermString();
            DevShader = datareader.ReadInt32();
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32();
            arg6 = datareader.ReadInt32();

            if (vcsFileVersion >= 64)
            {
                arg7 = datareader.ReadInt32();
            }

            var nr_of_arguments = datareader.ReadInt32();
            if (has_psrs_file)
            {
                // nr_of_arguments is overwritten
                nr_of_arguments = datareader.ReadInt32();
            }
            for (var i = 0; i < nr_of_arguments; i++)
            {
                var string_arg0 = datareader.ReadNullTermStringAtPosition();
                var string_arg1 = "";
                datareader.BaseStream.Position += 128;
                if (datareader.ReadInt32() > 0)
                {
                    string_arg1 = datareader.ReadNullTermStringAtPosition();
                    datareader.BaseStream.Position += 68;
                }
                mainParams.Add((string_arg0, string_arg1));
            }
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID0 (produces this file)"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID1 - usually a ref to the vs file ({VcsProgramType.VertexShader})"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID2 - usually a ref to the ps file ({VcsProgramType.PixelShader})"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID3"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID4"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID5"));
            editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID6"));

            if (vcsFileVersion >= 64)
            {
                if (has_psrs_file)
                {
                    editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID7 - ref to psrs file ({VcsProgramType.PixelShaderRenderState})"));
                    editorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                        $"// Editor ref. ID8 - common editor reference shared by multiple files "));
                }
                else
                {
                    editorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                        "// Editor ref. ID7- common editor reference shared by multiple files"));
                }
            }
        }

        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            var vcs_version = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"version {vcs_version}");
            datareader.BreakLine();
            datareader.ShowByteCount("features header");
            var has_psrs_file = 0;
            if (vcs_version >= 64)
            {
                has_psrs_file = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, "has_psrs_file = " + (has_psrs_file > 0 ? "True" : "False"));
            }
            var version = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"Version = {version}");
            var len_name_description = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"{len_name_description} len of name");
            datareader.BreakLine();
            var name_desc = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount(name_desc);
            datareader.ShowBytes(len_name_description + 1);
            datareader.BreakLine();
            datareader.ShowByteCount();
            datareader.ShowBytes(4, $"DevShader bool");
            var arg1 = datareader.ReadUInt32AtPosition(4);
            var arg2 = datareader.ReadUInt32AtPosition(8);
            var arg3 = datareader.ReadUInt32AtPosition(12);
            datareader.ShowBytes(12, 4, breakLine: false);
            datareader.TabComment($"({arg1},{arg2},{arg3})");
            var arg4 = datareader.ReadUInt32AtPosition(0);
            var arg5 = datareader.ReadUInt32AtPosition(4);
            var arg6 = datareader.ReadUInt32AtPosition(8);
            if (vcs_version >= 64)
            {
                var arg7 = datareader.ReadUInt32AtPosition(12);
                datareader.ShowBytes(16, 4, breakLine: false);
                datareader.TabComment($"({arg4},{arg5},{arg6},{arg7})");
            }
            else
            {
                datareader.ShowBytes(12, 4, breakLine: false);
                datareader.TabComment($"({arg4},{arg5},{arg6})");
            }

            datareader.BreakLine();
            datareader.ShowByteCount();
            var argument_count = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"argument_count = {argument_count}");
            if (has_psrs_file == 1)
            {
                // nr_of_arguments becomes overwritten
                argument_count = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, $"argument_count = {argument_count} (overridden)");
            }
            datareader.BreakLine();
            datareader.ShowByteCount();
            for (var i = 0; i < argument_count; i++)
            {
                var default_name = datareader.ReadNullTermStringAtPosition();
                datareader.Comment($"{default_name}");
                datareader.ShowBytes(128);
                var has_s_argument = datareader.ReadUInt32AtPosition();
                datareader.ShowBytes(4);
                if (has_s_argument > 0)
                {
                    var sSymbolArgValue = datareader.ReadUInt32AtPosition(64);
                    var sSymbolName = datareader.ReadNullTermStringAtPosition();
                    datareader.Comment($"{sSymbolName}");
                    datareader.ShowBytes(68);
                }
            }
            datareader.BreakLine();
            datareader.ShowByteCount("Editor/Shader stack for generating the file");
            datareader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"Editor ref. ID1 - usually a ref to the vs file ({VcsProgramType.VertexShader})");
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"Editor ref. ID2 - usually a ref to the ps file ({VcsProgramType.PixelShader})");
            datareader.ShowBytes(16, "Editor ref. ID3");
            datareader.ShowBytes(16, "Editor ref. ID4");
            datareader.ShowBytes(16, "Editor ref. ID5");
            datareader.ShowBytes(16, "Editor ref. ID6");
            if (vcs_version >= 64 && has_psrs_file == 0)
            {
                datareader.ShowBytes(16, "Editor ref. ID7 - common editor reference shared by multiple files");
            }
            if (vcs_version >= 64 && has_psrs_file == 1)
            {
                datareader.ShowBytes(16, $"Editor ref. ID7 - reference to psrs file ({VcsProgramType.PixelShaderRenderState})");
                datareader.ShowBytes(16, "Editor ref. ID8 - common editor reference shared by multiple files");
            }
            datareader.BreakLine();
        }
    }

    public class VsPsHeaderBlock : ShaderDataBlock
    {
        public int vcsFileVersion { get; }
        public bool hasPsrsFile { get; }
        public string fileID0 { get; }
        public string fileID1 { get; }
        public VsPsHeaderBlock(ShaderDataReader datareader) : base(datareader)
        {
            var vcsMagicId = datareader.ReadInt32();
            if (vcsMagicId != ShaderFile.MAGIC)
            {
                throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                    vcsMagicId, nameof(vcsMagicId));
            }

            vcsFileVersion = datareader.ReadInt32();
            ThrowIfNotSupported(vcsFileVersion);

            var psrs_arg = 0;
            if (vcsFileVersion >= 64)
            {
                psrs_arg = datareader.ReadInt32();
                if (psrs_arg != 0 && psrs_arg != 1)
                {
                    throw new ShaderParserException($"Unexpected value psrs_arg = {psrs_arg}");
                }
            }
            hasPsrsFile = psrs_arg > 0;
            fileID0 = datareader.ReadBytesAsString(16);
            fileID1 = datareader.ReadBytesAsString(16);
        }

        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            var vcs_version = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"version {vcs_version}");
            datareader.BreakLine();
            datareader.ShowByteCount("ps/vs header");
            if (vcs_version >= 64)
            {
                var has_psrs_file = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, $"has_psrs_file = {(has_psrs_file > 0 ? "True" : "False")}");
            }
            datareader.BreakLine();
            datareader.ShowByteCount("Editor/Shader stack for generating the file");
            datareader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
            datareader.ShowBytes(16, "Editor ref. ID1 - common editor reference shared by multiple files");
        }
    }

    // SfBlocks are usually 152 bytes long, occasionally they have extra string parameters
    public class SfBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public string name0 { get; }
        public string name1 { get; }
        public int arg0 { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; }
        public List<string> additionalParams { get; } = new();
        public SfBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            arg0 = datareader.ReadInt32();
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32AtPosition();
            var additionalStringsCount = datareader.ReadInt32();
            for (var i = 0; i < additionalStringsCount; i++)
            {
                additionalParams.Add(datareader.ReadNullTermString());
            }
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount();
            for (var i = 0; i < 2; i++)
            {
                var name1 = datareader.ReadNullTermStringAtPosition();
                if (name1.Length > 0)
                {
                    datareader.Comment($"{name1}");
                }
                datareader.ShowBytes(64);
            }
            var arg0 = datareader.ReadInt32AtPosition(0);
            var arg1 = datareader.ReadInt32AtPosition(4);
            var arg2 = datareader.ReadInt32AtPosition(8);
            var arg3 = datareader.ReadInt32AtPosition(12);
            var arg4 = datareader.ReadInt32AtPosition(16);
            var arg5 = datareader.ReadInt32AtPosition(20);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg0},{arg1},{arg2},{arg3})");
            datareader.ShowBytes(4, $"({arg4}) known values [-1,28]");
            datareader.ShowBytes(4, $"{arg5} additional string params");
            var string_offset = (int)datareader.BaseStream.Position;
            List<string> names = new();
            for (var i = 0; i < arg5; i++)
            {
                var paramname = datareader.ReadNullTermStringAtPosition(string_offset, rel: false);
                names.Add(paramname);
                string_offset += paramname.Length + 1;
            }
            if (names.Count > 0)
            {
                PrintStringList(names);
                datareader.ShowBytes(string_offset - (int)datareader.BaseStream.Position);
            }
            datareader.BreakLine();
        }
        private void PrintStringList(List<string> names)
        {
            if (names.Count == 0)
            {
                return;
            }
            datareader.OutputWrite($"// {names[0]}");
            for (var i = 1; i < names.Count; i++)
            {
                datareader.OutputWrite($", {names[i]}");
            }
            datareader.BreakLine();
        }
    }

    // SfConstraintsBlocks are always 472 bytes long
    public class SfConstraintsBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public int relRule { get; }  // 1 = dependency-rule (feature file), 2 = dependency-rule (other files), 3 = exclusion
        public int arg0 { get; } // this is just 1 for features files and 2 for all other files
        public int[] flags { get; }
        public int[] range0 { get; }
        public int[] range1 { get; }
        public int[] range2 { get; }
        public string description { get; }
        public SfConstraintsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            relRule = datareader.ReadInt32();
            arg0 = datareader.ReadInt32();
            // flags are at (8)
            flags = ReadByteFlags();
            // range 0 at (24)
            range0 = ReadIntRange();
            datareader.BaseStream.Position += 68 - range0.Length * 4;
            // range 1 at (92)
            range1 = ReadIntRange();

            datareader.BaseStream.Position += 60 - range1.Length * 4;
            // range 2 at (152)
            range2 = ReadIntRange();
            datareader.BaseStream.Position += 64 - range2.Length * 4;
            description = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (datareader.ReadInt32AtPosition() >= 0)
            {
                ints0.Add(datareader.ReadInt32());
            }
            return ints0.ToArray();
        }
        private int[] ReadByteFlags()
        {
            var count = 0;
            var savedPosition = datareader.BaseStream.Position;
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            var byteFlags = new int[count];
            datareader.BaseStream.Position = savedPosition;
            for (var i = 0; i < count; i++)
            {
                byteFlags[i] = datareader.ReadByte();
            }
            datareader.BaseStream.Position = savedPosition + 16;
            return byteFlags;
        }
        public string RelRuleDescribe()
        {
            return relRule == 3 ? "EXC(3)" : $"INC({relRule})";
        }
        public string GetByteFlagsAsString()
        {
            return CombineIntArray(flags);
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"SF-CONTRAINTS-BLOCK[{blockIndex}]");
            datareader.ShowBytes(216);
            var name1 = datareader.ReadNullTermStringAtPosition();
            datareader.OutputWriteLine($"[{datareader.BaseStream.Position}] {name1}");
            datareader.ShowBytes(256);
            datareader.BreakLine();
        }
    }

    // DBlocks are always 152 bytes long
    public class DBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public string name0 { get; }
        public string name1 { get; } // it looks like d-blocks might have the provision for 2 strings (but not seen in use)
        public int arg0 { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; }
        public DBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            arg0 = datareader.ReadInt32();
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32();
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            var dBlockName = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount($"D-BLOCK[{blockIndex}]");
            datareader.Comment(dBlockName);
            datareader.ShowBytes(128);
            datareader.ShowBytes(12, 4);
            datareader.ShowBytes(12);
            datareader.BreakLine();
        }
    }

    // DConstraintsBlock are always 472 bytes long
    public class DConstraintsBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public int relRule { get; }  // 2 = dependency-rule (other files), 3 = exclusion (1 not present, as in the compat-blocks)
        public int arg0 { get; } // ALWAYS 3 (for sf-constraint-blocks this value is 1 for features files and 2 for all other files)
        public int arg1 { get; } // arg1 at (88) sometimes has a value > -1 (in compat-blocks this value is always seen to be -1)
        public int[] flags { get; }
        public int[] range0 { get; }
        public int[] range1 { get; }
        public int[] range2 { get; }
        public string description { get; }

        public DConstraintsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            relRule = datareader.ReadInt32();
            arg0 = datareader.ReadInt32();
            if (arg0 != 3)
            {
                throw new ShaderParserException("unexpected value!");
            }
            // flags at (8)
            flags = ReadByteFlags();
            // range0 at (24)
            range0 = ReadIntRange();
            datareader.BaseStream.Position += 64 - range0.Length * 4;
            // integer at (88)
            arg1 = datareader.ReadInt32();
            // range1 at (92)
            range1 = ReadIntRange();
            datareader.BaseStream.Position += 60 - range1.Length * 4;
            // range1 at (152)
            range2 = ReadIntRange();
            datareader.BaseStream.Position += 64 - range2.Length * 4;
            // there is a provision here for a description, but for the dota2 archive this is always null
            description = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (datareader.ReadInt32AtPosition() >= 0)
            {
                ints0.Add(datareader.ReadInt32());
            }
            return ints0.ToArray();
        }
        private int[] ReadByteFlags()
        {
            var count = 0;
            var savedPosition = datareader.BaseStream.Position;
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            var byteFlags = new int[count];
            datareader.BaseStream.Position = savedPosition;
            for (var i = 0; i < count; i++)
            {
                byteFlags[i] = datareader.ReadByte();
            }
            datareader.BaseStream.Position = savedPosition;
            datareader.BaseStream.Position += 16;
            return byteFlags;
        }
        public string ReadByteFlagsAsString()
        {
            return CombineIntArray(flags);
        }
        public bool AllFlagsAre3()
        {
            var flagsAre3 = true;
            foreach (var flag in flags)
            {
                if (flag != 3)
                {
                    flagsAre3 = false;
                }
            }
            return flagsAre3;
        }
        public string GetConciseDescription(int[] usePadding = null)
        {
            int[] p = { 10, 8, 15, 5 };
            if (usePadding != null)
            {
                p = usePadding;
            }
            var relRuleKeyDesciption = $"{RelRuleDescribe().PadRight(p[0])}{CombineIntArray(range1).PadRight(p[1])}" +
                $"{CombineIntArray(flags, includeParenth: true).PadRight(p[2])}{CombineIntArray(range2).PadRight(p[3])}";
            return relRuleKeyDesciption;
        }
        public string GetResolvedNames(List<SfBlock> sfBlocks, List<DBlock> dBlocks)
        {
            List<string> names = new();
            for (var i = 0; i < flags.Length; i++)
            {
                if (flags[i] == 2)
                {
                    names.Add(sfBlocks[range0[i]].name0);
                    continue;
                }
                if (flags[i] == 3)
                {
                    names.Add(dBlocks[range0[i]].name0);
                    continue;
                }
                throw new ShaderParserException("this cannot happen!");
            }
            return CombineStringArray(names.ToArray());
        }
        public string RelRuleDescribe()
        {
            return relRule == 3 ? "EXC(3)" : $"INC({relRule})";
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"D-CONSTRAINTS-BLOCK[{blockIndex}]");
            datareader.ShowBytes(472);
            datareader.BreakLine();
        }
    }

    public class ParamBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public string name0 { get; }
        public string name1 { get; }
        public string name2 { get; }
        public int type { get; }
        public float res0 { get; }
        public int lead0 { get; }
        public byte[] dynExp { get; } = Array.Empty<byte>();
        public int arg0 { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; } = -1;
        public string fileref { get; }
        public int[] ranges0 { get; } = new int[4];
        public int[] ranges1 { get; } = new int[4];
        public int[] ranges2 { get; } = new int[4];
        public float[] ranges3 { get; } = new float[4];
        public float[] ranges4 { get; } = new float[4];
        public float[] ranges5 { get; } = new float[4];
        public int[] ranges6 { get; } = new int[4];
        public int[] ranges7 { get; } = new int[4];
        public string command0 { get; }
        public string command1 { get; }
        public byte[] v65Data { get; } = Array.Empty<byte>();
        public ParamBlock(ShaderDataReader datareader, int blockIndex, int vcsVersion) : base(datareader)
        {
            this.blockIndex = blockIndex;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            type = datareader.ReadInt32();
            res0 = datareader.ReadSingle();
            name2 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            lead0 = datareader.ReadInt32();
            if (lead0 == 6 || lead0 == 7)
            {
                var dynExpLen = datareader.ReadInt32();
                dynExp = datareader.ReadBytes(dynExpLen);
            }

            arg0 = datareader.ReadInt32();

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            if (arg0 == 0x534D4253)
            {
                // note - bytes are ignored
                var dynExpLength = datareader.ReadInt32();
                datareader.ReadBytes(dynExpLength);

                arg0 = datareader.ReadInt32();
            }

            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            if (vcsVersion > 62)
            {
                arg5 = datareader.ReadInt32();
            }

            fileref = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            for (var i = 0; i < 4; i++)
            {
                ranges0[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges1[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges2[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges3[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges4[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges5[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges6[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                ranges7[i] = datareader.ReadInt32();
            }
            command0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;
            command1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;

            if (vcsVersion >= 65)
            {
                v65Data = datareader.ReadBytes(6);
            }
        }

        public void PrintByteDetail(int vcsVersion)
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"PARAM-BLOCK[{blockIndex}]");
            var name1 = datareader.ReadNullTermStringAtPosition();
            datareader.OutputWriteLine($"// {name1}");
            datareader.ShowBytes(64);
            var name2 = datareader.ReadNullTermStringAtPosition();
            if (name2.Length > 0)
            {
                datareader.OutputWriteLine($"// {name2}");
            }
            datareader.ShowBytes(64);
            datareader.ShowBytes(8);
            var name3 = datareader.ReadNullTermStringAtPosition();
            if (name3.Length > 0)
            {
                datareader.OutputWriteLine($"// {name3}");
            }
            datareader.ShowBytes(64);
            var paramType = datareader.ReadUInt32AtPosition();
            datareader.OutputWriteLine($"// param-type, 6 or 7 lead dynamic-exp. Known values: 0,1,5,6,7,8,10,11,13");
            datareader.ShowBytes(4);
            if (paramType == 6 || paramType == 7)
            {
                var dynLength = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment("dyn-exp len", 1);
                datareader.TabComment("dynamic expression");
                datareader.ShowBytes(dynLength);
            }

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            var checkSBMS = datareader.ReadBytesAtPosition(0, 4);
            if (checkSBMS[0] == 0x53 && checkSBMS[1] == 0x42 && checkSBMS[2] == 0x4D && checkSBMS[3] == 0x53)
            {
                datareader.ShowBytes(4, "SBMS");
                var dynLength = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, "dyn-exp len");
                datareader.ShowBytes(dynLength, "dynamic expression", 1);
            }

            // 5 or 6 int arguments follow depending on version
            datareader.ShowBytes(20, 4);
            // v64,65 has an additional argument
            if (vcsVersion >= 64)
            {
                datareader.ShowBytes(4);
            }

            // a rarely seen file reference
            var name4 = datareader.ReadNullTermStringAtPosition();
            if (name4.Length > 0)
            {
                datareader.OutputWriteLine($"// {name4}");
            }
            datareader.ShowBytes(64);
            // float or int arguments
            var a0 = datareader.ReadInt32AtPosition(0);
            var a1 = datareader.ReadInt32AtPosition(4);
            var a2 = datareader.ReadInt32AtPosition(8);
            var a3 = datareader.ReadInt32AtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = datareader.ReadInt32AtPosition(0);
            a1 = datareader.ReadInt32AtPosition(4);
            a2 = datareader.ReadInt32AtPosition(8);
            a3 = datareader.ReadInt32AtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = datareader.ReadInt32AtPosition(0);
            a1 = datareader.ReadInt32AtPosition(4);
            a2 = datareader.ReadInt32AtPosition(8);
            a3 = datareader.ReadInt32AtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            var f0 = datareader.ReadSingleAtPosition(0);
            var f1 = datareader.ReadSingleAtPosition(4);
            var f2 = datareader.ReadSingleAtPosition(8);
            var f3 = datareader.ReadSingleAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            f0 = datareader.ReadSingleAtPosition(0);
            f1 = datareader.ReadSingleAtPosition(4);
            f2 = datareader.ReadSingleAtPosition(8);
            f3 = datareader.ReadSingleAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            f0 = datareader.ReadSingleAtPosition(0);
            f1 = datareader.ReadSingleAtPosition(4);
            f2 = datareader.ReadSingleAtPosition(8);
            f3 = datareader.ReadSingleAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            a0 = datareader.ReadInt32AtPosition(0);
            a1 = datareader.ReadInt32AtPosition(4);
            a2 = datareader.ReadInt32AtPosition(8);
            a3 = datareader.ReadInt32AtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = datareader.ReadInt32AtPosition(0);
            a1 = datareader.ReadInt32AtPosition(4);
            a2 = datareader.ReadInt32AtPosition(8);
            a3 = datareader.ReadInt32AtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            // a command word, or pair of these
            var name5 = datareader.ReadNullTermStringAtPosition();
            if (name5.Length > 0)
            {
                datareader.OutputWriteLine($"// {name5}");
            }
            datareader.ShowBytes(32);
            var name6 = datareader.ReadNullTermStringAtPosition();
            if (name6.Length > 0)
            {
                datareader.OutputWriteLine($"// {name6}");
            }
            datareader.ShowBytes(32);

            if (vcsVersion == 65)
            {
                datareader.ShowBytes(6, "unknown bytes specific to vcs version 65");
            }

            datareader.BreakLine();
        }
        private static string Fmt(float val)
        {
            if (val == -1e9) return "-inf";
            if (val == 1e9) return "inf";
            return $"{val}";
        }
        private static string Fmt(int val)
        {
            if (val == -999999999) return "-inf";
            if (val == 999999999) return "inf";
            return "" + val; ;
        }
    }

    // MipmapBlocks are always 280 bytes long
    public class MipmapBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public string name { get; }
        public byte[] arg0 { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; }

        public MipmapBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            arg0 = datareader.ReadBytes(4);
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32();
            name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"MIPMAP-BLOCK[{blockIndex}]");
            datareader.ShowBytes(24, 4);
            var name1 = datareader.ReadNullTermStringAtPosition();
            datareader.Comment($"{name1}");
            datareader.ShowBytes(256);
            datareader.BreakLine();
        }
    }

    public class BufferBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public string name { get; }
        public int bufferSize { get; }
        public int arg0 { get; }
        public int paramCount { get; }
        public List<(string, int, int, int, int)> bufferParams { get; } = new();
        public uint blockCrc { get; }
        public BufferBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            bufferSize = datareader.ReadInt32();
            // datareader.MoveOffset(4); // these 4 bytes are always 0
            arg0 = datareader.ReadInt32();
            paramCount = datareader.ReadInt32();
            for (var i = 0; i < paramCount; i++)
            {
                var paramName = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                var bufferIndex = datareader.ReadInt32();
                var arg0 = datareader.ReadInt32();
                var arg1 = datareader.ReadInt32();
                var arg2 = datareader.ReadInt32();
                bufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
            }
            blockCrc = datareader.ReadUInt32();
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            var blockname = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount($"BUFFER-BLOCK[{blockIndex}] {blockname}");
            datareader.ShowBytes(64);
            var bufferSize = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{bufferSize} buffer-size");
            datareader.ShowBytes(4);
            var paramCount = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{paramCount} param-count");
            for (var i = 0; i < paramCount; i++)
            {
                var paramname = datareader.ReadNullTermStringAtPosition();
                datareader.OutputWriteLine($"// {paramname}");
                datareader.ShowBytes(64);
                var paramIndex = datareader.ReadUInt32AtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment($"{paramIndex} buffer-offset", 28);
                var vertexSize = datareader.ReadUInt32AtPosition();
                var attributeCount = datareader.ReadUInt32AtPosition(4);
                var size = datareader.ReadUInt32AtPosition(8);
                datareader.ShowBytes(12, $"({vertexSize},{attributeCount},{size}) (vertex-size, attribute-count, length)");
            }
            datareader.BreakLine();
            datareader.ShowBytes(4, "bufferID (some kind of crc/check)");
            datareader.BreakLine();
            datareader.BreakLine();
        }
    }

    public class VertexSymbolsBlock : ShaderDataBlock
    {
        public int blockIndex { get; }
        public int symbolsCount { get; }
        public List<(string, string, string, int)> symbolsDefinition { get; } = new();

        public VertexSymbolsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            this.blockIndex = blockIndex;
            symbolsCount = datareader.ReadInt32();
            for (var i = 0; i < symbolsCount; i++)
            {
                var name = datareader.ReadNullTermString();
                var type = datareader.ReadNullTermString();
                var option = datareader.ReadNullTermString();
                var semanticIndex = datareader.ReadInt32();
                symbolsDefinition.Add((name, type, option, semanticIndex));
            }
        }
        public void PrintByteDetail()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"SYMBOL-NAMES-BLOCK[{blockIndex}]");
            var symbolGroupCount = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{symbolGroupCount} string groups in this block");
            for (var i = 0; i < symbolGroupCount; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    var symbolname = datareader.ReadNullTermStringAtPosition();
                    datareader.OutputWriteLine($"// {symbolname}");
                    datareader.ShowBytes(symbolname.Length + 1);
                }
                datareader.ShowBytes(4);
                datareader.BreakLine();
            }
            if (symbolGroupCount == 0) datareader.BreakLine();
        }

    }
}

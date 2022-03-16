using System;
using System.Collections.Generic;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class FeaturesHeaderBlock : ShaderDataBlock
    {
        public int vcsFileVersion { get; }
        public bool has_psrs_file { get; }
        public int unknown_val { get; }
        public string file_description { get; }
        public int arg0 { get; }
        public int arg1 { get; }
        public int arg2 { get; }
        public int arg3 { get; }
        public int arg4 { get; }
        public int arg5 { get; }
        public int arg6 { get; }
        public int arg7 { get; }
        public List<(string, string)> mainParams { get; } = new();
        public List<(string, string)> editorIDs { get; } = new();
        public FeaturesHeaderBlock(ShaderDataReader datareader) : base(datareader)
        {
            int vcsMagicId = datareader.ReadInt32();
            if (vcsMagicId != ShaderFile.MAGIC)
            {
                throw new ShaderParserException($"Wrong file id {vcsMagicId:x}");
            }
            vcsFileVersion = datareader.ReadInt32();
            if (vcsFileVersion != 64 && vcsFileVersion != 65)
            {
                throw new ShaderParserException($"Unsupported version {vcsFileVersion}, only versions 64 and 65 are supported");
            }
            int psrs_arg = datareader.ReadInt32();
            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"unexpected value psrs_arg = {psrs_arg}");
            }
            has_psrs_file = psrs_arg > 0;
            unknown_val = datareader.ReadInt32();
            datareader.ReadInt32(); // length of name, but not needed because it's always null-term
            file_description = datareader.ReadNullTermString();
            arg0 = datareader.ReadInt32();
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32();
            arg6 = datareader.ReadInt32();
            arg7 = datareader.ReadInt32();
            int nr_of_arguments = datareader.ReadInt32();
            if (has_psrs_file)
            {
                // nr_of_arguments is overwritten
                nr_of_arguments = datareader.ReadInt32();
            }
            for (int i = 0; i < nr_of_arguments; i++)
            {
                string string_arg0 = datareader.ReadNullTermStringAtPosition();
                string string_arg1 = "";
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
            if (has_psrs_file)
            {
                editorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID7 - ref to psrs file ({VcsProgramType.PixelShaderRenderState})"));
                editorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                    $"// Editor ref. ID8 - common editor reference shared by multiple files "));
            } else
            {
                editorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                    "// Editor ref. ID7- common editor reference shared by multiple files"));
            }
        }

        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            datareader.ShowBytes(4, "version (64 or 65)");
            datareader.BreakLine();
            datareader.ShowByteCount("features header");
            int has_psrs_file = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, "has_psrs_file = " + (has_psrs_file > 0 ? "True" : "False"));
            int unknown_val = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"unknown_val = {unknown_val} (usually 0)");
            int len_name_description = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"{len_name_description} len of name");
            datareader.BreakLine();
            string name_desc = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount(name_desc);
            datareader.ShowBytes(len_name_description + 1);
            datareader.BreakLine();
            datareader.ShowByteCount();
            uint arg1 = datareader.ReadUInt32AtPosition(0);
            uint arg2 = datareader.ReadUInt32AtPosition(4);
            uint arg3 = datareader.ReadUInt32AtPosition(8);
            uint arg4 = datareader.ReadUInt32AtPosition(12);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg1},{arg2},{arg3},{arg4})");
            uint arg5 = datareader.ReadUInt32AtPosition(0);
            uint arg6 = datareader.ReadUInt32AtPosition(4);
            uint arg7 = datareader.ReadUInt32AtPosition(8);
            uint arg8 = datareader.ReadUInt32AtPosition(12);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg5},{arg6},{arg7},{arg8})");
            datareader.BreakLine();
            datareader.ShowByteCount();
            int nr_of_arguments = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"nr of arguments {nr_of_arguments}");
            if (has_psrs_file == 1)
            {
                // nr_of_arguments is overwritten
                nr_of_arguments = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, $"nr of arguments overriden ({nr_of_arguments})");
            }
            datareader.BreakLine();
            datareader.ShowByteCount();
            for (int i = 0; i < nr_of_arguments; i++)
            {
                string default_name = datareader.ReadNullTermStringAtPosition();
                datareader.Comment($"{default_name}");
                datareader.ShowBytes(128);
                uint has_s_argument = datareader.ReadUInt32AtPosition();
                datareader.ShowBytes(4);
                if (has_s_argument > 0)
                {
                    uint sSymbolArgValue = datareader.ReadUInt32AtPosition(64);
                    string sSymbolName = datareader.ReadNullTermStringAtPosition();
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
            if (has_psrs_file == 0)
            {
                datareader.ShowBytes(16,
                    "Editor ref. ID7 - ID appears to be shared across archives for vcs files with the same minor-version");
            }
            if (has_psrs_file == 1)
            {
                datareader.ShowBytes(16, $"Editor ref. ID7 - reference to psrs file ({VcsProgramType.PixelShaderRenderState})");
                datareader.ShowBytes(16,
                    "Editor ref. ID7 - ID appears to be shared across archives for vcs files with the same minor-version");
            }
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
            int magic = datareader.ReadInt32();
            if (magic != ShaderFile.MAGIC)
            {
                throw new ShaderParserException($"Wrong file id {magic:x} (not a vcs2 file)");
            }
            vcsFileVersion = datareader.ReadInt32();
            if (vcsFileVersion != 64 && vcsFileVersion != 65)
            {
                throw new ShaderParserException($"Unsupported version {vcsFileVersion}, only versions 64 and 65 are supported");
            }
            int psrs_arg = datareader.ReadInt32();
            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"Unexpected value psrs_arg = {psrs_arg}");
            }
            hasPsrsFile = psrs_arg > 0;
            fileID0 = datareader.ReadBytesAsString(16);
            fileID1 = datareader.ReadBytesAsString(16);
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            datareader.ShowBytes(4, "version (64 or 65)");
            datareader.BreakLine();
            datareader.ShowByteCount("ps/vs header");
            int has_psrs_file = datareader.ReadInt32AtPosition();
            datareader.ShowBytes(4, $"has_psrs_file = {(has_psrs_file > 0 ? "True" : "False")}");
            datareader.BreakLine();
            datareader.ShowByteCount("Editor/Shader stack for generating the file");
            datareader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
            datareader.ShowBytes(16,
                    "Editor ref. ID1 - this ID is shared across archives for vcs files with the same minor-version");
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
            int additionalStringsCount = datareader.ReadInt32();
            for (int i = 0; i < additionalStringsCount; i++)
            {
                additionalParams.Add(datareader.ReadNullTermString());
            }
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount();
            for (int i = 0; i < 2; i++)
            {
                string name1 = datareader.ReadNullTermStringAtPosition();
                if (name1.Length > 0)
                {
                    datareader.Comment($"{name1}");
                }
                datareader.ShowBytes(64);
            }
            int arg0 = datareader.ReadInt32AtPosition(0);
            int arg1 = datareader.ReadInt32AtPosition(4);
            int arg2 = datareader.ReadInt32AtPosition(8);
            int arg3 = datareader.ReadInt32AtPosition(12);
            int arg4 = datareader.ReadInt32AtPosition(16);
            int arg5 = datareader.ReadInt32AtPosition(20);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg0},{arg1},{arg2},{arg3})");
            datareader.ShowBytes(4, $"({arg4}) known values [-1,28]");
            datareader.ShowBytes(4, $"{arg5} additional string params");
            int string_offset = (int)datareader.BaseStream.Position;
            List<string> names = new();
            for (int i = 0; i < arg5; i++)
            {
                string paramname = datareader.ReadNullTermStringAtPosition(string_offset, rel: false);
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
            for (int i = 1; i < names.Count; i++)
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
            int count = 0;
            long savedPosition = datareader.BaseStream.Position;
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            int[] byteFlags = new int[count];
            datareader.BaseStream.Position = savedPosition;
            for (int i = 0; i < count; i++)
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
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"SF-CONTRAINTS-BLOCK[{blockIndex}]");
            datareader.ShowBytes(216);
            string name1 = datareader.ReadNullTermStringAtPosition();
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
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            string dBlockName = datareader.ReadNullTermStringAtPosition();
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
            int count = 0;
            long savedPosition = datareader.BaseStream.Position;
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            int[] byteFlags = new int[count];
            datareader.BaseStream.Position = savedPosition;
            for (int i = 0; i < count; i++)
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
            bool flagsAre3 = true;
            foreach (int flag in flags)
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
            string relRuleKeyDesciption = $"{RelRuleDescribe().PadRight(p[0])}{CombineIntArray(range1).PadRight(p[1])}" +
                $"{CombineIntArray(flags, includeParenth: true).PadRight(p[2])}{CombineIntArray(range2).PadRight(p[3])}";
            return relRuleKeyDesciption;
        }
        public string GetResolvedNames(List<SfBlock> sfBlocks, List<DBlock> dBlocks)
        {
            List<string> names = new();
            for (int i = 0; i < flags.Length; i++)
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
        public void PrintAnnotatedBytestream()
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
        public int arg5 { get; }
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
                int dynExpLen = datareader.ReadInt32();
                dynExp = datareader.ReadBytes(dynExpLen);
            }

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            byte[] checkSBMS = datareader.ReadBytesAtPosition(0, 4);
            if (checkSBMS[0] == 0x53 && checkSBMS[1] == 0x42 && checkSBMS[2] == 0x4D && checkSBMS[3] == 0x53)
            {
                // note - bytes are ignored
                datareader.ReadBytes(4);
                int dynExpLength = datareader.ReadInt32();
                datareader.ReadBytes(dynExpLength);
            }

            arg0 = datareader.ReadInt32();
            arg1 = datareader.ReadInt32();
            arg2 = datareader.ReadInt32();
            arg3 = datareader.ReadInt32();
            arg4 = datareader.ReadInt32();
            arg5 = datareader.ReadInt32();
            fileref = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            for (int i = 0; i < 4; i++)
            {
                ranges0[i] = datareader.ReadInt32();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges1[i] = datareader.ReadInt32();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges2[i] = datareader.ReadInt32();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges3[i] = datareader.ReadSingle();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges4[i] = datareader.ReadSingle();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges5[i] = datareader.ReadSingle();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges6[i] = datareader.ReadInt32();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges7[i] = datareader.ReadInt32();
            }
            command0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;
            command1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;

            if (vcsVersion == 65)
            {
                v65Data = datareader.ReadBytes(6);
            }
        }

        public void PrintAnnotatedBytestream(int vcsVersion)
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"PARAM-BLOCK[{blockIndex}]");
            string name1 = datareader.ReadNullTermStringAtPosition();
            datareader.OutputWriteLine($"// {name1}");
            datareader.ShowBytes(64);
            string name2 = datareader.ReadNullTermStringAtPosition();
            if (name2.Length > 0)
            {
                datareader.OutputWriteLine($"// {name2}");
            }
            datareader.ShowBytes(64);
            datareader.ShowBytes(8);
            string name3 = datareader.ReadNullTermStringAtPosition();
            if (name3.Length > 0)
            {
                datareader.OutputWriteLine($"// {name3}");
            }
            datareader.ShowBytes(64);
            uint paramType = datareader.ReadUInt32AtPosition();
            datareader.OutputWriteLine($"// param-type, 6 or 7 lead dynamic-exp. Known values: 0,1,5,6,7,8,10,11,13");
            datareader.ShowBytes(4);
            if (paramType == 6 || paramType == 7)
            {
                int dynLength = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment("dyn-exp len", 1);
                datareader.TabComment("dynamic expression");
                datareader.ShowBytes(dynLength);
            }

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            byte[] checkSBMS = datareader.ReadBytesAtPosition(0, 4);
            if (checkSBMS[0] == 0x53 && checkSBMS[1] == 0x42 && checkSBMS[2] == 0x4D && checkSBMS[3] == 0x53)
            {
                datareader.ShowBytes(4, "SBMS");
                int dynLength = datareader.ReadInt32AtPosition();
                datareader.ShowBytes(4, "dyn-exp len");
                datareader.ShowBytes(dynLength, "dynamic expression", 1);
            }


            // 6 int parameters follow here
            datareader.ShowBytes(24, 4);
            // a rarely seen file reference
            string name4 = datareader.ReadNullTermStringAtPosition();
            if (name4.Length > 0)
            {
                datareader.OutputWriteLine($"// {name4}");
            }
            datareader.ShowBytes(64);
            // float or int arguments
            int a0 = datareader.ReadInt32AtPosition(0);
            int a1 = datareader.ReadInt32AtPosition(4);
            int a2 = datareader.ReadInt32AtPosition(8);
            int a3 = datareader.ReadInt32AtPosition(12);
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
            float f0 = datareader.ReadSingleAtPosition(0);
            float f1 = datareader.ReadSingleAtPosition(4);
            float f2 = datareader.ReadSingleAtPosition(8);
            float f3 = datareader.ReadSingleAtPosition(12);
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
            string name5 = datareader.ReadNullTermStringAtPosition();
            if (name5.Length > 0)
            {
                datareader.OutputWriteLine($"// {name5}");
            }
            datareader.ShowBytes(32);
            string name6 = datareader.ReadNullTermStringAtPosition();
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
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"MIPMAP-BLOCK[{blockIndex}]");
            datareader.ShowBytes(24, 4);
            string name1 = datareader.ReadNullTermStringAtPosition();
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
            for (int i = 0; i < paramCount; i++)
            {
                string paramName = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                int bufferIndex = datareader.ReadInt32();
                int arg0 = datareader.ReadInt32();
                int arg1 = datareader.ReadInt32();
                int arg2 = datareader.ReadInt32();
                bufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
            }
            blockCrc = datareader.ReadUInt32();
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            string blockname = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount($"BUFFER-BLOCK[{blockIndex}] {blockname}");
            datareader.ShowBytes(64);
            uint bufferSize = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{bufferSize} buffer-size");
            datareader.ShowBytes(4);
            uint paramCount = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{paramCount} param-count");
            for (int i = 0; i < paramCount; i++)
            {
                string paramname = datareader.ReadNullTermStringAtPosition();
                datareader.OutputWriteLine($"// {paramname}");
                datareader.ShowBytes(64);
                uint paramIndex = datareader.ReadUInt32AtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment($"{paramIndex} buffer-offset", 28);
                uint vertexSize = datareader.ReadUInt32AtPosition();
                uint attributeCount = datareader.ReadUInt32AtPosition(4);
                uint size = datareader.ReadUInt32AtPosition(8);
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
            for (int i = 0; i < symbolsCount; i++)
            {
                string name = datareader.ReadNullTermString();
                string type = datareader.ReadNullTermString();
                string option = datareader.ReadNullTermString();
                int semanticIndex = datareader.ReadInt32();
                symbolsDefinition.Add((name, type, option, semanticIndex));
            }
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.BaseStream.Position = start;
            datareader.ShowByteCount($"SYMBOL-NAMES-BLOCK[{blockIndex}]");
            uint symbolGroupCount = datareader.ReadUInt32AtPosition();
            datareader.ShowBytes(4, $"{symbolGroupCount} string groups in this block");
            for (int i = 0; i < symbolGroupCount; i++)
            {
                for (int j = 0; j < 3; j++)
                {
                    string symbolname = datareader.ReadNullTermStringAtPosition();
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

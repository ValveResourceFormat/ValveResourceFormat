using System;
using System.Collections.Generic;
using ValveResourceFormat.Utils;
using static ValveResourceFormat.CompiledShader.ShaderUtilHelpers;

namespace ValveResourceFormat.CompiledShader
{
    public class FeaturesHeaderBlock : ShaderDataBlock
    {
        public int VcsFileVersion { get; }
        public bool HasPsrsFile { get; }
        public int Version { get; }
        public string FileDescription { get; }
        public int DevShader { get; }
        public int Arg1 { get; }
        public int Arg2 { get; }
        public int Arg3 { get; }
        public int Arg4 { get; }
        public int Arg5 { get; }
        public int Arg6 { get; }
        public int Arg7 { get; } = -1;
        public List<(string, string)> MainParams { get; } = new();
        public List<(string, string)> EditorIDs { get; } = new();
        public FeaturesHeaderBlock(ShaderDataReader datareader) : base(datareader)
        {
            var vcsMagicId = datareader.ReadInt32();
            if (vcsMagicId != ShaderFile.MAGIC)
            {
                throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                    vcsMagicId, nameof(vcsMagicId));
            }

            VcsFileVersion = datareader.ReadInt32();
            ThrowIfNotSupported(VcsFileVersion);

            var psrs_arg = 0;
            if (VcsFileVersion >= 64)
            {
                psrs_arg = datareader.ReadInt32();

                // S&box - there is no psrs here. Just a 2 and a 0
                if (psrs_arg == 2)
                {
                    psrs_arg = 0;
                    datareader.BaseStream.Position += 4;
                    VcsFileVersion = 64;
                }
            }

            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"unexpected value psrs_arg = {psrs_arg}");
            }
            HasPsrsFile = psrs_arg > 0;
            Version = datareader.ReadInt32();
            datareader.ReadInt32(); // length of name, but not needed because it's always null-term
            FileDescription = datareader.ReadNullTermString();
            DevShader = datareader.ReadInt32();
            Arg1 = datareader.ReadInt32();
            Arg2 = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            Arg4 = datareader.ReadInt32();
            Arg5 = datareader.ReadInt32();
            Arg6 = datareader.ReadInt32();

            if (VcsFileVersion >= 64)
            {
                Arg7 = datareader.ReadInt32();
            }

            var nr_of_arguments = datareader.ReadInt32();
            if (HasPsrsFile)
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
                MainParams.Add((string_arg0, string_arg1));
            }
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID0 (produces this file)"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID1 - usually a ref to the vs file ({VcsProgramType.VertexShader})"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID2 - usually a ref to the ps file ({VcsProgramType.PixelShader})"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID3"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID4"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID5"));
            EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", "// Editor ref. ID6"));

            if (VcsFileVersion >= 64)
            {
                if (HasPsrsFile)
                {
                    EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref. ID7 - ref to psrs file ({VcsProgramType.PixelShaderRenderState})"));
                    EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                        $"// Editor ref. ID8 - common editor reference shared by multiple files "));
                }
                else
                {
                    EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}",
                        "// Editor ref. ID7- common editor reference shared by multiple files"));
                }
            }
        }

        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount("vcs file");
            DataReader.ShowBytes(4, "\"vcs2\"");
            var vcs_version = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"version {vcs_version}");
            DataReader.BreakLine();
            DataReader.ShowByteCount("features header");
            var has_psrs_file = 0;
            if (vcs_version >= 64)
            {
                has_psrs_file = DataReader.ReadInt32AtPosition();
                if (has_psrs_file == 2)
                {
                    DataReader.ShowBytes(4, "s&box const int 2");
                    DataReader.ShowBytes(4, "s&box int");
                    vcs_version = 64;
                }
                else
                {
                    DataReader.ShowBytes(4, "has_psrs_file = " + (has_psrs_file > 0 ? "True" : "False"));
                }
            }
            var version = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"Version = {version}");
            var len_name_description = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"{len_name_description} len of name");
            DataReader.BreakLine();
            var name_desc = DataReader.ReadNullTermStringAtPosition();
            DataReader.ShowByteCount(name_desc);
            DataReader.ShowBytes(len_name_description + 1);
            DataReader.BreakLine();
            DataReader.ShowByteCount();
            DataReader.ShowBytes(4, $"DevShader bool");
            var arg1 = DataReader.ReadUInt32AtPosition(4);
            var arg2 = DataReader.ReadUInt32AtPosition(8);
            var arg3 = DataReader.ReadUInt32AtPosition(12);
            DataReader.ShowBytes(12, 4, breakLine: false);
            DataReader.TabComment($"({arg1},{arg2},{arg3})");
            var arg4 = DataReader.ReadUInt32AtPosition(0);
            var arg5 = DataReader.ReadUInt32AtPosition(4);
            var arg6 = DataReader.ReadUInt32AtPosition(8);
            if (vcs_version >= 64)
            {
                var arg7 = DataReader.ReadUInt32AtPosition(12);
                DataReader.ShowBytes(16, 4, breakLine: false);
                DataReader.TabComment($"({arg4},{arg5},{arg6},{arg7})");
            }
            else
            {
                DataReader.ShowBytes(12, 4, breakLine: false);
                DataReader.TabComment($"({arg4},{arg5},{arg6})");
            }

            DataReader.BreakLine();
            DataReader.ShowByteCount();
            var argument_count = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"argument_count = {argument_count}");
            if (has_psrs_file == 1)
            {
                // nr_of_arguments becomes overwritten
                argument_count = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, $"argument_count = {argument_count} (overridden)");
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount();
            for (var i = 0; i < argument_count; i++)
            {
                var default_name = DataReader.ReadNullTermStringAtPosition();
                DataReader.Comment($"{default_name}");
                DataReader.ShowBytes(128);
                var has_s_argument = DataReader.ReadUInt32AtPosition();
                DataReader.ShowBytes(4);
                if (has_s_argument > 0)
                {
                    var sSymbolArgValue = DataReader.ReadUInt32AtPosition(64);
                    var sSymbolName = DataReader.ReadNullTermStringAtPosition();
                    DataReader.Comment($"{sSymbolName}");
                    DataReader.ShowBytes(68);
                }
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount("Editor/Shader stack for generating the file");
            DataReader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"Editor ref. ID1 - usually a ref to the vs file ({VcsProgramType.VertexShader})");
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"Editor ref. ID2 - usually a ref to the ps file ({VcsProgramType.PixelShader})");
            DataReader.ShowBytes(16, "Editor ref. ID3");
            DataReader.ShowBytes(16, "Editor ref. ID4");
            DataReader.ShowBytes(16, "Editor ref. ID5");
            DataReader.ShowBytes(16, "Editor ref. ID6");
            if (vcs_version >= 64 && has_psrs_file == 0)
            {
                DataReader.ShowBytes(16, "Editor ref. ID7 - common editor reference shared by multiple files");
            }
            if (vcs_version >= 64 && has_psrs_file == 1)
            {
                DataReader.ShowBytes(16, $"Editor ref. ID7 - reference to psrs file ({VcsProgramType.PixelShaderRenderState})");
                DataReader.ShowBytes(16, "Editor ref. ID8 - common editor reference shared by multiple files");
            }
            DataReader.BreakLine();
        }
    }

    public class VsPsHeaderBlock : ShaderDataBlock
    {
        public int VcsFileVersion { get; }
        public bool HasPsrsFile { get; }
        public string FileID0 { get; }
        public string FileID1 { get; }
        public VsPsHeaderBlock(ShaderDataReader datareader) : base(datareader)
        {
            var vcsMagicId = datareader.ReadInt32();
            if (vcsMagicId != ShaderFile.MAGIC)
            {
                throw new UnexpectedMagicException($"Wrong magic ID, VCS expects 0x{ShaderFile.MAGIC:x}",
                    vcsMagicId, nameof(vcsMagicId));
            }

            VcsFileVersion = datareader.ReadInt32();
            ThrowIfNotSupported(VcsFileVersion);

            var psrs_arg = 0;
            if (VcsFileVersion >= 64)
            {
                psrs_arg = datareader.ReadInt32();

                // S&box - there is no psrs here. Just a 2 and a 0
                if (psrs_arg == 2)
                {
                    psrs_arg = 0;
                    datareader.BaseStream.Position += 4;
                    VcsFileVersion = 64;
                }

                if (psrs_arg != 0 && psrs_arg != 1)
                {
                    throw new ShaderParserException($"Unexpected value psrs_arg = {psrs_arg}");
                }
            }
            HasPsrsFile = psrs_arg > 0;
            FileID0 = datareader.ReadBytesAsString(16);
            FileID1 = datareader.ReadBytesAsString(16);
        }

        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount("vcs file");
            DataReader.ShowBytes(4, "\"vcs2\"");
            var vcs_version = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"version {vcs_version}");
            DataReader.BreakLine();
            DataReader.ShowByteCount("ps/vs header");
            if (vcs_version >= 64)
            {
                var has_psrs_file = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, $"has_psrs_file = {(has_psrs_file > 0 ? "True" : "False")}");
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount("Editor/Shader stack for generating the file");
            DataReader.ShowBytes(16, "Editor ref. ID0 (produces this file)");
            DataReader.ShowBytes(16, "Editor ref. ID1 - common editor reference shared by multiple files");
        }
    }

    // SfBlocks are usually 152 bytes long, occasionally they have extra string parameters
    public class SfBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name0 { get; }
        public string Name1 { get; }
        public int Arg0 { get; }
        public int Arg1 { get; }
        public int Arg2 { get; }
        public int Arg3 { get; }
        public int Arg4 { get; }
        public int Arg5 { get; }
        public List<string> AdditionalParams { get; } = new();
        public SfBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Arg0 = datareader.ReadInt32();
            Arg1 = datareader.ReadInt32();
            Arg2 = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            Arg4 = datareader.ReadInt32();
            Arg5 = datareader.ReadInt32AtPosition();
            var additionalStringsCount = datareader.ReadInt32();
            for (var i = 0; i < additionalStringsCount; i++)
            {
                AdditionalParams.Add(datareader.ReadNullTermString());
            }
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount();
            for (var i = 0; i < 2; i++)
            {
                var name1 = DataReader.ReadNullTermStringAtPosition();
                if (name1.Length > 0)
                {
                    DataReader.Comment($"{name1}");
                }
                DataReader.ShowBytes(64);
            }
            var arg0 = DataReader.ReadInt32AtPosition(0);
            var arg1 = DataReader.ReadInt32AtPosition(4);
            var arg2 = DataReader.ReadInt32AtPosition(8);
            var arg3 = DataReader.ReadInt32AtPosition(12);
            var arg4 = DataReader.ReadInt32AtPosition(16);
            var arg5 = DataReader.ReadInt32AtPosition(20);
            DataReader.ShowBytes(16, 4, breakLine: false);
            DataReader.TabComment($"({arg0},{arg1},{arg2},{arg3})");
            DataReader.ShowBytes(4, $"({arg4}) known values [-1,28]");
            DataReader.ShowBytes(4, $"{arg5} additional string params");
            var string_offset = (int)DataReader.BaseStream.Position;
            List<string> names = new();
            for (var i = 0; i < arg5; i++)
            {
                var paramname = DataReader.ReadNullTermStringAtPosition(string_offset, rel: false);
                names.Add(paramname);
                string_offset += paramname.Length + 1;
            }
            if (names.Count > 0)
            {
                PrintStringList(names);
                DataReader.ShowBytes(string_offset - (int)DataReader.BaseStream.Position);
            }
            DataReader.BreakLine();
        }
        private void PrintStringList(List<string> names)
        {
            if (names.Count == 0)
            {
                return;
            }
            DataReader.OutputWrite($"// {names[0]}");
            for (var i = 1; i < names.Count; i++)
            {
                DataReader.OutputWrite($", {names[i]}");
            }
            DataReader.BreakLine();
        }
    }

    // SfConstraintsBlocks are always 472 bytes long
    public class SfConstraintsBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public int RelRule { get; }  // 1 = dependency-rule (feature file), 2 = dependency-rule (other files), 3 = exclusion
        public int Arg0 { get; } // this is just 1 for features files and 2 for all other files
        public int[] Flags { get; }
        public int[] Range0 { get; }
        public int[] Range1 { get; }
        public int[] Range2 { get; }
        public string Description { get; }
        public SfConstraintsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            RelRule = datareader.ReadInt32();
            Arg0 = datareader.ReadInt32();
            // flags are at (8)
            Flags = ReadByteFlags();
            // range 0 at (24)
            Range0 = ReadIntRange();
            datareader.BaseStream.Position += 68 - Range0.Length * 4;
            // range 1 at (92)
            Range1 = ReadIntRange();

            datareader.BaseStream.Position += 60 - Range1.Length * 4;
            // range 2 at (152)
            Range2 = ReadIntRange();
            datareader.BaseStream.Position += 64 - Range2.Length * 4;
            Description = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (DataReader.ReadInt32AtPosition() >= 0)
            {
                ints0.Add(DataReader.ReadInt32());
            }
            return ints0.ToArray();
        }
        private int[] ReadByteFlags()
        {
            var count = 0;
            var savedPosition = DataReader.BaseStream.Position;
            while (DataReader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            var byteFlags = new int[count];
            DataReader.BaseStream.Position = savedPosition;
            for (var i = 0; i < count; i++)
            {
                byteFlags[i] = DataReader.ReadByte();
            }
            DataReader.BaseStream.Position = savedPosition + 16;
            return byteFlags;
        }
        public string RelRuleDescribe()
        {
            return RelRule == 3 ? "EXC(3)" : $"INC({RelRule})";
        }
        public string GetByteFlagsAsString()
        {
            return CombineIntArray(Flags);
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"SF-CONTRAINTS-BLOCK[{BlockIndex}]");
            DataReader.ShowBytes(216);
            var name1 = DataReader.ReadNullTermStringAtPosition();
            DataReader.OutputWriteLine($"[{DataReader.BaseStream.Position}] {name1}");
            DataReader.ShowBytes(256);
            DataReader.BreakLine();
        }
    }

    // DBlocks are always 152 bytes long
    public class DBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name0 { get; }
        public string Name1 { get; } // it looks like d-blocks might have the provision for 2 strings (but not seen in use)
        public int Arg0 { get; }
        public int Arg1 { get; }
        public int Arg2 { get; }
        public int Arg3 { get; }
        public int Arg4 { get; }
        public int Arg5 { get; }
        public DBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Arg0 = datareader.ReadInt32();
            Arg1 = datareader.ReadInt32();
            Arg2 = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            Arg4 = datareader.ReadInt32();
            Arg5 = datareader.ReadInt32();
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            var dBlockName = DataReader.ReadNullTermStringAtPosition();
            DataReader.ShowByteCount($"D-BLOCK[{BlockIndex}]");
            DataReader.Comment(dBlockName);
            DataReader.ShowBytes(128);
            DataReader.ShowBytes(12, 4);
            DataReader.ShowBytes(12);
            DataReader.BreakLine();
        }
    }

    // DConstraintsBlock are always 472 bytes long
    public class DConstraintsBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public int RelRule { get; }  // 2 = dependency-rule (other files), 3 = exclusion (1 not present, as in the compat-blocks)
        public int Arg0 { get; } // ALWAYS 3 (for sf-constraint-blocks this value is 1 for features files and 2 for all other files)
        public int Arg1 { get; } // arg1 at (88) sometimes has a value > -1 (in compat-blocks this value is always seen to be -1)
        public int[] Flags { get; }
        public int[] Range0 { get; }
        public int[] Range1 { get; }
        public int[] Range2 { get; }
        public string Description { get; }

        public DConstraintsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            RelRule = datareader.ReadInt32();
            Arg0 = datareader.ReadInt32();
            if (Arg0 != 3)
            {
                throw new ShaderParserException("unexpected value!");
            }
            // flags at (8)
            Flags = ReadByteFlags();
            // range0 at (24)
            Range0 = ReadIntRange();
            datareader.BaseStream.Position += 64 - Range0.Length * 4;
            // integer at (88)
            Arg1 = datareader.ReadInt32();
            // range1 at (92)
            Range1 = ReadIntRange();
            datareader.BaseStream.Position += 60 - Range1.Length * 4;
            // range1 at (152)
            Range2 = ReadIntRange();
            datareader.BaseStream.Position += 64 - Range2.Length * 4;
            // there is a provision here for a description, but for the dota2 archive this is always null
            Description = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (DataReader.ReadInt32AtPosition() >= 0)
            {
                ints0.Add(DataReader.ReadInt32());
            }
            return ints0.ToArray();
        }
        private int[] ReadByteFlags()
        {
            var count = 0;
            var savedPosition = DataReader.BaseStream.Position;
            while (DataReader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            var byteFlags = new int[count];
            DataReader.BaseStream.Position = savedPosition;
            for (var i = 0; i < count; i++)
            {
                byteFlags[i] = DataReader.ReadByte();
            }
            DataReader.BaseStream.Position = savedPosition;
            DataReader.BaseStream.Position += 16;
            return byteFlags;
        }
        public string ReadByteFlagsAsString()
        {
            return CombineIntArray(Flags);
        }
        public bool AllFlagsAre3()
        {
            var flagsAre3 = true;
            foreach (var flag in Flags)
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
            var relRuleKeyDesciption = $"{RelRuleDescribe().PadRight(p[0])}{CombineIntArray(Range1).PadRight(p[1])}" +
                $"{CombineIntArray(Flags, includeParenth: true).PadRight(p[2])}{CombineIntArray(Range2).PadRight(p[3])}";
            return relRuleKeyDesciption;
        }
        public string GetResolvedNames(List<SfBlock> sfBlocks, List<DBlock> dBlocks)
        {
            List<string> names = new();
            for (var i = 0; i < Flags.Length; i++)
            {
                if (Flags[i] == 2)
                {
                    names.Add(sfBlocks[Range0[i]].Name0);
                    continue;
                }
                if (Flags[i] == 3)
                {
                    names.Add(dBlocks[Range0[i]].Name0);
                    continue;
                }
                throw new ShaderParserException("this cannot happen!");
            }
            return CombineStringArray(names.ToArray());
        }
        public string RelRuleDescribe()
        {
            return RelRule == 3 ? "EXC(3)" : $"INC({RelRule})";
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"D-CONSTRAINTS-BLOCK[{BlockIndex}]");
            DataReader.ShowBytes(472);
            DataReader.BreakLine();
        }
    }

    public class ParamBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name0 { get; }
        public string Name1 { get; }
        public string Name2 { get; }
        public int Type { get; }
        public float Res0 { get; }
        public int Lead0 { get; }
        public byte[] DynExp { get; } = Array.Empty<byte>();
        public int Arg0 { get; }
        public int Arg1 { get; }
        public int Arg2 { get; }
        public int Arg3 { get; }
        public int Arg4 { get; }
        public int Arg5 { get; } = -1;
        public string FileRef { get; }
        public int[] Ranges0 { get; } = new int[4];
        public int[] Ranges1 { get; } = new int[4];
        public int[] Ranges2 { get; } = new int[4];
        public float[] Ranges3 { get; } = new float[4];
        public float[] Ranges4 { get; } = new float[4];
        public float[] Ranges5 { get; } = new float[4];
        public int[] Ranges6 { get; } = new int[4];
        public int[] Ranges7 { get; } = new int[4];
        public string Command0 { get; }
        public string Command1 { get; }
        public byte[] V65Data { get; } = Array.Empty<byte>();
        public ParamBlock(ShaderDataReader datareader, int blockIndex, int vcsVersion) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Name1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Type = datareader.ReadInt32();
            Res0 = datareader.ReadSingle();
            Name2 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Lead0 = datareader.ReadInt32();
            if (Lead0 == 6 || Lead0 == 7)
            {
                var dynExpLen = datareader.ReadInt32();
                DynExp = datareader.ReadBytes(dynExpLen);
            }

            Arg0 = datareader.ReadInt32();

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            if (Arg0 == 0x534D4253)
            {
                // note - bytes are ignored
                var dynExpLength = datareader.ReadInt32();
                datareader.ReadBytes(dynExpLength);

                Arg0 = datareader.ReadInt32();
            }

            Arg1 = datareader.ReadInt32();
            Arg2 = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            Arg4 = datareader.ReadInt32();
            if (vcsVersion > 62)
            {
                Arg5 = datareader.ReadInt32();
            }

            FileRef = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            for (var i = 0; i < 4; i++)
            {
                Ranges0[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges1[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges2[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges3[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges4[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges5[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges6[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                Ranges7[i] = datareader.ReadInt32();
            }
            Command0 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;
            Command1 = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;

            if (vcsVersion >= 65)
            {
                V65Data = datareader.ReadBytes(6);
            }
        }

        public void PrintByteDetail(int vcsVersion)
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"PARAM-BLOCK[{BlockIndex}]");
            var name1 = DataReader.ReadNullTermStringAtPosition();
            DataReader.OutputWriteLine($"// {name1}");
            DataReader.ShowBytes(64);
            var name2 = DataReader.ReadNullTermStringAtPosition();
            if (name2.Length > 0)
            {
                DataReader.OutputWriteLine($"// {name2}");
            }
            DataReader.ShowBytes(64);
            DataReader.ShowBytes(8);
            var name3 = DataReader.ReadNullTermStringAtPosition();
            if (name3.Length > 0)
            {
                DataReader.OutputWriteLine($"// {name3}");
            }
            DataReader.ShowBytes(64);
            var paramType = DataReader.ReadUInt32AtPosition();
            DataReader.OutputWriteLine($"// param-type, 6 or 7 lead dynamic-exp. Known values: 0,1,5,6,7,8,10,11,13");
            DataReader.ShowBytes(4);
            if (paramType == 6 || paramType == 7)
            {
                var dynLength = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, breakLine: false);
                DataReader.TabComment("dyn-exp len", 1);
                DataReader.TabComment("dynamic expression");
                DataReader.ShowBytes(dynLength);
            }

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            var checkSBMS = DataReader.ReadBytesAtPosition(0, 4);
            if (checkSBMS[0] == 0x53 && checkSBMS[1] == 0x42 && checkSBMS[2] == 0x4D && checkSBMS[3] == 0x53)
            {
                DataReader.ShowBytes(4, "SBMS");
                var dynLength = DataReader.ReadInt32AtPosition();
                DataReader.ShowBytes(4, "dyn-exp len");
                DataReader.ShowBytes(dynLength, "dynamic expression", 1);
            }

            // 5 or 6 int arguments follow depending on version
            DataReader.ShowBytes(20, 4);
            // v64,65 has an additional argument
            if (vcsVersion >= 64)
            {
                DataReader.ShowBytes(4);
            }

            // a rarely seen file reference
            var name4 = DataReader.ReadNullTermStringAtPosition();
            if (name4.Length > 0)
            {
                DataReader.OutputWriteLine($"// {name4}");
            }
            DataReader.ShowBytes(64);
            // float or int arguments
            var a0 = DataReader.ReadInt32AtPosition(0);
            var a1 = DataReader.ReadInt32AtPosition(4);
            var a2 = DataReader.ReadInt32AtPosition(8);
            var a3 = DataReader.ReadInt32AtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = DataReader.ReadInt32AtPosition(0);
            a1 = DataReader.ReadInt32AtPosition(4);
            a2 = DataReader.ReadInt32AtPosition(8);
            a3 = DataReader.ReadInt32AtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = DataReader.ReadInt32AtPosition(0);
            a1 = DataReader.ReadInt32AtPosition(4);
            a2 = DataReader.ReadInt32AtPosition(8);
            a3 = DataReader.ReadInt32AtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            var f0 = DataReader.ReadSingleAtPosition(0);
            var f1 = DataReader.ReadSingleAtPosition(4);
            var f2 = DataReader.ReadSingleAtPosition(8);
            var f3 = DataReader.ReadSingleAtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            f0 = DataReader.ReadSingleAtPosition(0);
            f1 = DataReader.ReadSingleAtPosition(4);
            f2 = DataReader.ReadSingleAtPosition(8);
            f3 = DataReader.ReadSingleAtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            f0 = DataReader.ReadSingleAtPosition(0);
            f1 = DataReader.ReadSingleAtPosition(4);
            f2 = DataReader.ReadSingleAtPosition(8);
            f3 = DataReader.ReadSingleAtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"floats ({Fmt(f0)},{Fmt(f1)},{Fmt(f2)},{Fmt(f3)})", 10);
            a0 = DataReader.ReadInt32AtPosition(0);
            a1 = DataReader.ReadInt32AtPosition(4);
            a2 = DataReader.ReadInt32AtPosition(8);
            a3 = DataReader.ReadInt32AtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            a0 = DataReader.ReadInt32AtPosition(0);
            a1 = DataReader.ReadInt32AtPosition(4);
            a2 = DataReader.ReadInt32AtPosition(8);
            a3 = DataReader.ReadInt32AtPosition(12);
            DataReader.ShowBytes(16, breakLine: false);
            DataReader.TabComment($"ints   ({Fmt(a0)},{Fmt(a1)},{Fmt(a2)},{Fmt(a3)})", 10);
            // a command word, or pair of these
            var name5 = DataReader.ReadNullTermStringAtPosition();
            if (name5.Length > 0)
            {
                DataReader.OutputWriteLine($"// {name5}");
            }
            DataReader.ShowBytes(32);
            var name6 = DataReader.ReadNullTermStringAtPosition();
            if (name6.Length > 0)
            {
                DataReader.OutputWriteLine($"// {name6}");
            }
            DataReader.ShowBytes(32);

            if (vcsVersion == 65)
            {
                DataReader.ShowBytes(6, "unknown bytes specific to vcs version 65");
            }

            DataReader.BreakLine();
        }
        private static string Fmt(float val)
        {
            if (val == -1e9)
            {
                return "-inf";
            }

            if (val == 1e9)
            {
                return "inf";
            }

            return $"{val}";
        }
        private static string Fmt(int val)
        {
            if (val == -999999999)
            {
                return "-inf";
            }

            if (val == 999999999)
            {
                return "inf";
            }

            return "" + val; ;
        }
    }

    // MipmapBlocks are always 280 bytes long
    public class MipmapBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public byte[] Arg0 { get; }
        public int Arg1 { get; }
        public int Arg2 { get; }
        public int Arg3 { get; }
        public int Arg4 { get; }
        public int Arg5 { get; }

        public MipmapBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Arg0 = datareader.ReadBytes(4);
            Arg1 = datareader.ReadInt32();
            Arg2 = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            Arg4 = datareader.ReadInt32();
            Arg5 = datareader.ReadInt32();
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"MIPMAP-BLOCK[{BlockIndex}]");
            DataReader.ShowBytes(24, 4);
            var name1 = DataReader.ReadNullTermStringAtPosition();
            DataReader.Comment($"{name1}");
            DataReader.ShowBytes(256);
            DataReader.BreakLine();
        }
    }

    public class BufferBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public int BufferSize { get; }
        public int Arg0 { get; }
        public int ParamCount { get; }
        public List<(string, int, int, int, int)> BufferParams { get; } = new();
        public uint BlockCrc { get; }
        public BufferBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            BufferSize = datareader.ReadInt32();
            // datareader.MoveOffset(4); // these 4 bytes are always 0
            Arg0 = datareader.ReadInt32();
            ParamCount = datareader.ReadInt32();
            for (var i = 0; i < ParamCount; i++)
            {
                var paramName = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                var bufferIndex = datareader.ReadInt32();
                var arg0 = datareader.ReadInt32();
                var arg1 = datareader.ReadInt32();
                var arg2 = datareader.ReadInt32();
                BufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
            }
            BlockCrc = datareader.ReadUInt32();
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            var blockname = DataReader.ReadNullTermStringAtPosition();
            DataReader.ShowByteCount($"BUFFER-BLOCK[{BlockIndex}] {blockname}");
            DataReader.ShowBytes(64);
            var bufferSize = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{bufferSize} buffer-size");
            DataReader.ShowBytes(4);
            var paramCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{paramCount} param-count");
            for (var i = 0; i < paramCount; i++)
            {
                var paramname = DataReader.ReadNullTermStringAtPosition();
                DataReader.OutputWriteLine($"// {paramname}");
                DataReader.ShowBytes(64);
                var paramIndex = DataReader.ReadUInt32AtPosition();
                DataReader.ShowBytes(4, breakLine: false);
                DataReader.TabComment($"{paramIndex} buffer-offset", 28);
                var vertexSize = DataReader.ReadUInt32AtPosition();
                var attributeCount = DataReader.ReadUInt32AtPosition(4);
                var size = DataReader.ReadUInt32AtPosition(8);
                DataReader.ShowBytes(12, $"({vertexSize},{attributeCount},{size}) (vertex-size, attribute-count, length)");
            }
            DataReader.BreakLine();
            DataReader.ShowBytes(4, "bufferID (some kind of crc/check)");
            DataReader.BreakLine();
            DataReader.BreakLine();
        }
    }

    public class VertexSymbolsBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public int SymbolsCount { get; }
        public List<(string, string, string, int)> SymbolsDefinition { get; } = new();

        public VertexSymbolsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            SymbolsCount = datareader.ReadInt32();
            for (var i = 0; i < SymbolsCount; i++)
            {
                var name = datareader.ReadNullTermString();
                var type = datareader.ReadNullTermString();
                var option = datareader.ReadNullTermString();
                var semanticIndex = datareader.ReadInt32();
                SymbolsDefinition.Add((name, type, option, semanticIndex));
            }
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"SYMBOL-NAMES-BLOCK[{BlockIndex}]");
            var symbolGroupCount = DataReader.ReadUInt32AtPosition();
            DataReader.ShowBytes(4, $"{symbolGroupCount} string groups in this block");
            for (var i = 0; i < symbolGroupCount; i++)
            {
                for (var j = 0; j < 3; j++)
                {
                    var symbolname = DataReader.ReadNullTermStringAtPosition();
                    DataReader.OutputWriteLine($"// {symbolname}");
                    DataReader.ShowBytes(symbolname.Length + 1);
                }
                DataReader.ShowBytes(4);
                DataReader.BreakLine();
            }
            if (symbolGroupCount == 0)
            {
                DataReader.BreakLine();
            }
        }

    }
}

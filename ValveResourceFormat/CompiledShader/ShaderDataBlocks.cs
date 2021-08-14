using System;
using System.Collections.Generic;
using System.Diagnostics;
using static ValveResourceFormat.ShaderParser.ShaderUtilHelpers;

#pragma warning disable CA1051 // Do not declare visible instance fields
#pragma warning disable CA1024 // Use properties where appropriate
namespace ValveResourceFormat.ShaderParser
{
    public class FeaturesHeaderBlock : ShaderDataBlock
    {
        public int fileversion;
        public bool hasPsrsFile;
        public int unknown_val;
        public string file_description;
        public int arg0;
        public int arg1;
        public int arg2;
        public int arg3;
        public int arg4;
        public int arg5;
        public int arg6;
        public int arg7;
        public List<(string, string)> mainParams = new();
        public List<string> fileIDs = new();
        public FeaturesHeaderBlock(ShaderDataReader datareader, long start) : base(datareader, start)
        {
            int magic = datareader.ReadInt();
            if (magic != 0x32736376)
            {
                throw new ShaderParserException($"wrong file id {magic:x}");
            }
            fileversion = datareader.ReadInt();
            if (fileversion != 64)
            {
                throw new ShaderParserException($"wrong version {fileversion}, expecting 64");
            }
            int psrs_arg = datareader.ReadInt();
            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"unexpected value psrs_arg = {psrs_arg}");
            }
            hasPsrsFile = psrs_arg > 0;
            unknown_val = datareader.ReadInt();
            datareader.ReadInt(); // length of name, but not needed because it's always null-term
            file_description = datareader.ReadNullTermString();
            arg0 = datareader.ReadInt();
            arg1 = datareader.ReadInt();
            arg2 = datareader.ReadInt();
            arg3 = datareader.ReadInt();
            arg4 = datareader.ReadInt();
            arg5 = datareader.ReadInt();
            arg6 = datareader.ReadInt();
            arg7 = datareader.ReadInt();
            int nr_of_arguments = datareader.ReadInt();
            if (hasPsrsFile)
            {
                // nr_of_arguments is overwritten
                nr_of_arguments = datareader.ReadInt();
            }
            for (int i = 0; i < nr_of_arguments; i++)
            {
                string string_arg0 = datareader.ReadNullTermStringAtPosition();
                string string_arg1 = "";
                datareader.MoveOffset(128);
                if (datareader.ReadInt() > 0)
                {
                    string_arg1 = datareader.ReadNullTermStringAtPosition();
                    datareader.MoveOffset(68);
                }
                mainParams.Add((string_arg0, string_arg1));
            }
            for (int i = 0; i < 8; i++)
            {
                fileIDs.Add(datareader.ReadBytesAsString(16).Replace(" ", "").ToLower());
            }
            if (hasPsrsFile)
            {
                fileIDs.Add(datareader.ReadBytesAsString(16).Replace(" ", "").ToLower());
            }
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            datareader.ShowBytes(4, "version 64");
            datareader.BreakLine();
            datareader.ShowByteCount("features header");
            int has_psrs_file = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, "has_psrs_file = " + (has_psrs_file > 0 ? "True" : "False"));
            int unknown_val = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"unknown_val = {unknown_val} (usually 0)");
            int len_name_description = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"{len_name_description} len of name");
            datareader.BreakLine();
            string name_desc = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount(name_desc);
            datareader.ShowBytes(len_name_description + 1);
            datareader.BreakLine();
            datareader.ShowByteCount();
            uint arg1 = datareader.ReadUIntAtPosition(0);
            uint arg2 = datareader.ReadUIntAtPosition(4);
            uint arg3 = datareader.ReadUIntAtPosition(8);
            uint arg4 = datareader.ReadUIntAtPosition(12);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg1},{arg2},{arg3},{arg4})");
            uint arg5 = datareader.ReadUIntAtPosition(0);
            uint arg6 = datareader.ReadUIntAtPosition(4);
            uint arg7 = datareader.ReadUIntAtPosition(8);
            uint arg8 = datareader.ReadUIntAtPosition(12);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg5},{arg6},{arg7},{arg8})");
            datareader.BreakLine();
            datareader.ShowByteCount();
            int nr_of_arguments = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"nr of arguments {nr_of_arguments}");
            if (has_psrs_file == 1)
            {
                // nr_of_arguments is overwritten
                nr_of_arguments = datareader.ReadIntAtPosition();
                datareader.ShowBytes(4, $"nr of arguments overriden ({nr_of_arguments})");
            }
            datareader.BreakLine();
            datareader.ShowByteCount();
            for (int i = 0; i < nr_of_arguments; i++)
            {
                string default_name = datareader.ReadNullTermStringAtPosition();
                datareader.Comment($"{default_name}");
                datareader.ShowBytes(128);
                uint has_s_argument = datareader.ReadUIntAtPosition();
                datareader.ShowBytes(4);
                if (has_s_argument > 0)
                {
                    uint sSymbolArgValue = datareader.ReadUIntAtPosition(64);
                    string sSymbolName = datareader.ReadNullTermStringAtPosition();
                    datareader.Comment($"{sSymbolName}");
                    datareader.ShowBytes(68);
                }
            }
            datareader.BreakLine();
            datareader.ShowByteCount("File IDs");
            datareader.ShowBytes(16, "file ID0");
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment("file ID1 - ref to vs file");
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment("file ID2 - ref to ps file");
            datareader.ShowBytes(16, "file ID3");
            datareader.ShowBytes(16, "file ID4");
            datareader.ShowBytes(16, "file ID5");
            datareader.ShowBytes(16, "file ID6");
            if (has_psrs_file == 0)
            {
                datareader.ShowBytes(16, "file ID7 - shared by all Dota2 vcs files");
            }
            if (has_psrs_file == 1)
            {
                datareader.ShowBytes(16, "file ID7 - reference to psrs file");
                datareader.ShowBytes(16, "file ID8 - shared by all Dota2 vcs files");
            }
            datareader.BreakLine();
        }
    }

    public class VsPsHeaderBlock : ShaderDataBlock
    {
        public int fileversion;
        public bool hasPsrsFile;
        public string fileID0;
        public string fileID1;
        public VsPsHeaderBlock(ShaderDataReader datareader, long start) : base(datareader, start)
        {
            int magic = datareader.ReadInt();
            if (magic != 0x32736376)
            {
                throw new ShaderParserException($"wrong file id {magic:x}");
            }
            fileversion = datareader.ReadInt();
            if (fileversion != 64)
            {
                throw new ShaderParserException($"wrong version {fileversion}, expecting 64");
            }
            int psrs_arg = datareader.ReadInt();
            if (psrs_arg != 0 && psrs_arg != 1)
            {
                throw new ShaderParserException($"unexpected value psrs_arg = {psrs_arg}");
            }
            hasPsrsFile = psrs_arg > 0;
            fileID0 = datareader.ReadBytesAsString(16).Replace(" ", "").ToLower();
            fileID1 = datareader.ReadBytesAsString(16).Replace(" ", "").ToLower();
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            datareader.ShowByteCount("vcs file");
            datareader.ShowBytes(4, "\"vcs2\"");
            datareader.ShowBytes(4, "version 64");
            datareader.BreakLine();
            datareader.ShowByteCount("ps/vs header");
            int has_psrs_file = datareader.ReadIntAtPosition();
            datareader.ShowBytes(4, $"has_psrs_file = {(has_psrs_file > 0 ? "True" : "False")}");
            datareader.ShowBytes(16, "file ID0");
            datareader.ShowBytes(16, "file ID1 - shared by all Dota2 vcs files");
            datareader.BreakLine();
        }
    }

    public class SfBlock : ShaderDataBlock
    {
        public int blockId;
        public string name0;
        public string name1;
        public int arg0;
        public int arg1;
        public int arg2; // layers
        public int arg3;
        public int arg4;
        public int arg5;
        public List<string> additionalParams = new();
        public SfBlock(ShaderDataReader datareader, long start, int blockId) : base(datareader, start)
        {
            this.blockId = blockId;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            arg0 = datareader.ReadInt();
            arg1 = datareader.ReadInt();
            arg2 = datareader.ReadInt();
            arg3 = datareader.ReadInt();
            arg4 = datareader.ReadInt();
            arg5 = datareader.ReadIntAtPosition();
            int additionalStringsCount = datareader.ReadInt();
            for (int i = 0; i < additionalStringsCount; i++)
            {
                additionalParams.Add(datareader.ReadNullTermString());
            }
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
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
            int arg0 = datareader.ReadIntAtPosition(0);
            int arg1 = datareader.ReadIntAtPosition(4);
            int arg2 = datareader.ReadIntAtPosition(8);
            int arg3 = datareader.ReadIntAtPosition(12);
            int arg4 = datareader.ReadIntAtPosition(16);
            int arg5 = datareader.ReadIntAtPosition(20);
            datareader.ShowBytes(16, 4, breakLine: false);
            datareader.TabComment($"({arg0},{arg1},{arg2},{arg3})");
            datareader.ShowBytes(4, $"({arg4}) known values [-1,28]");
            datareader.ShowBytes(4, $"{arg5} additional string params");
            long string_offset = datareader.GetOffset();
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
                datareader.ShowBytes((int)(string_offset - datareader.GetOffset()));
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

    public class SfConstraintsBlock : ShaderDataBlock
    {
        public int blockIndex;
        public int relRule;  // 1 = dependency (feature file), 2 = dependency (other files), 3 = exclusion
        public int arg0; // this is just 1 for features files and 2 for all other files
        public int[] flags;
        public int[] range0;
        public int[] range1;
        public int[] range2;
        public string description;
        public SfConstraintsBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            relRule = datareader.ReadInt();
            arg0 = datareader.ReadInt();
            // flags are at (8)
            flags = ReadByteFlags();
            // range 0 at (24)
            range0 = ReadIntRange();
            datareader.MoveOffset(68 - range0.Length * 4);
            // range 1 at (92)
            range1 = ReadIntRange();
            datareader.MoveOffset(60 - range1.Length * 4);
            // range 2 at (152)
            range2 = ReadIntRange();
            datareader.MoveOffset(64 - range2.Length * 4);
            description = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(256);
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (datareader.ReadIntAtPosition() >= 0)
            {
                ints0.Add(datareader.ReadInt());
            }
            return ints0.ToArray();
        }
        // 1 to 5 byte flags occur at position 8 (there is provision for a maximum of 16 byte-flags)
        private int[] ReadByteFlags()
        {
            int count = 0;
            datareader.SavePosition();
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            int[] byteFlags = new int[count];
            for (int i = 0; i < count; i++)
            {
                byteFlags[i] = datareader.ReadByte();
            }
            datareader.RestorePosition();
            datareader.MoveOffset(16);
            return byteFlags;
        }
        public string GetRelRuleDescription()
        {
            return relRule == 3 ? "EXC(3)" : $"INC({relRule})";
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            datareader.ShowByteCount($"COMPAT-BLOCK[{blockIndex}]");
            datareader.ShowBytes(216);
            string name1 = datareader.ReadNullTermStringAtPosition();
            datareader.OutputWriteLine($"[{datareader.GetOffset()}] {name1}");
            datareader.ShowBytes(256);
            datareader.BreakLine();
        }
    }

    public class DBlock : ShaderDataBlock
    {
        public int blockIndex;
        public string name0;
        public string name1;
        public int arg0;
        public int arg1;
        public int arg2;
        public int arg3;
        public int arg4;
        public int arg5;
        public DBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            arg0 = datareader.ReadInt();
            arg1 = datareader.ReadInt();
            arg2 = datareader.ReadInt();
            arg3 = datareader.ReadInt();
            arg4 = datareader.ReadInt();
            arg5 = datareader.ReadInt();
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            string dBlockName = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount($"DBLOCK[{blockIndex}]");
            datareader.Comment(dBlockName);
            datareader.ShowBytes(128);
            datareader.ShowBytes(12, 4);
            datareader.ShowBytes(12);
            datareader.BreakLine();
        }
    }

    public class DConstraintsBlock : ShaderDataBlock
    {
        public int blockIndex;
        public int relRule;  // 2 = dependency (other files), 3 = exclusion (1 not present, as in the compat-blocks)
        public int arg0; // ALWAYS 3 (for compat-blocks, this value is 1 for features files and 2 for all other files)
        public int arg1; // arg1 at (88) sometimes has a value > -1 (in compat-blocks this value is always seen to be -1)
        public int[] flags;
        public int[] range0;
        public int[] range1;
        public int[] range2;
        public string description;
        public DConstraintsBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            relRule = datareader.ReadInt();
            arg0 = datareader.ReadInt();
            if (arg0 != 3)
            {
                throw new ShaderParserException("unexpected value!");
            }
            // flags at (8)
            flags = ReadByteFlags();
            // range0 at (24)
            range0 = ReadIntRange();
            datareader.MoveOffset(64 - range0.Length * 4);
            // an integer at (88)
            arg1 = datareader.ReadInt();
            // range1 at (92)
            range1 = ReadIntRange();
            datareader.MoveOffset(60 - range1.Length * 4);
            // range1 at (152)
            range2 = ReadIntRange();
            datareader.MoveOffset(64 - range2.Length * 4);
            // there is a provision here for a description, but for the dota2 archive it is always null
            description = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(256);
        }
        private int[] ReadIntRange()
        {
            List<int> ints0 = new();
            while (datareader.ReadIntAtPosition() >= 0)
            {
                ints0.Add(datareader.ReadInt());
            }
            return ints0.ToArray();
        }
        private int[] ReadByteFlags()
        {
            int count = 0;
            datareader.SavePosition();
            while (datareader.ReadByte() > 0 && count < 16)
            {
                count++;
            }
            int[] byteFlags = new int[count];
            for (int i = 0; i < count; i++)
            {
                byteFlags[i] = datareader.ReadByte();
            }
            datareader.RestorePosition();
            datareader.MoveOffset(16);
            return byteFlags;
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
            datareader.SetPosition(start);
            datareader.ShowByteCount($"D-RULE-BLOCK[{blockIndex}]");
            datareader.ShowBytes(472);
            datareader.BreakLine();
        }
    }

    public class ParamBlock : ShaderDataBlock
    {
        public int blockId;
        public string name0;
        public string name1;
        public string name2;
        public int pt0;
        public float res0;
        public int main0;
        public byte[] dynExp = Array.Empty<byte>();
        public int arg0;
        public int arg1;
        public int arg2;
        public int arg3;
        public int arg4;
        public int arg5;
        public string fileref;
        public int[] ranges0 = new int[4];
        public int[] ranges1 = new int[4];
        public int[] ranges2 = new int[4];
        public float[] ranges3 = new float[4];
        public float[] ranges4 = new float[4];
        public float[] ranges5 = new float[4];
        public int[] ranges6 = new int[4];
        public int[] ranges7 = new int[4];
        public string command0;
        public string command1;

        public ParamBlock(ShaderDataReader datareader, long start, int blockId) : base(datareader, start)
        {
            this.blockId = blockId;
            name0 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            name1 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            pt0 = datareader.ReadInt();
            res0 = datareader.ReadFloat();
            name2 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            main0 = datareader.ReadInt();
            if (main0 == 6 || main0 == 7)
            {
                int dynExpLen = datareader.ReadInt();
                dynExp = datareader.ReadBytes(dynExpLen);
            }
            arg0 = datareader.ReadInt();
            arg1 = datareader.ReadInt();
            arg2 = datareader.ReadInt();
            arg3 = datareader.ReadInt();
            arg4 = datareader.ReadInt();
            arg5 = datareader.ReadInt();
            fileref = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            for (int i = 0; i < 4; i++)
            {
                ranges0[i] = datareader.ReadInt();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges1[i] = datareader.ReadInt();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges2[i] = datareader.ReadInt();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges3[i] = datareader.ReadFloat();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges4[i] = datareader.ReadFloat();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges5[i] = datareader.ReadFloat();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges6[i] = datareader.ReadInt();
            }
            for (int i = 0; i < 4; i++)
            {
                ranges7[i] = datareader.ReadInt();
            }
            command0 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(32);
            command1 = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(32);
        }

        public void ShowBlock()
        {
            Debug.WriteLine($"name0 {new string(' ', 20)} {name0}");
            Debug.WriteLine($"name1 {new string(' ', 20)} {name1}");
            Debug.WriteLine($"lead0,lead1 {new string(' ', 24)} ({pt0},{res0})");
            Debug.WriteLine($"name2 {new string(' ', 20)} {name2}");
            Debug.WriteLine($"paramType {new string(' ', 16)} {main0}");
            Debug.WriteLine($"dynExp {new string(' ', 1)} {ShaderDataReader.BytesToString(dynExp)}");
            Debug.WriteLine($"arg0 {new string(' ', 21)} {arg0,9}");
            Debug.WriteLine($"arg1 {new string(' ', 21)} {arg1,9}");
            Debug.WriteLine($"arg2 {new string(' ', 21)} {arg2,9}");
            Debug.WriteLine($"arg3 {new string(' ', 21)} {arg3,9}");
            Debug.WriteLine($"arg4 {new string(' ', 21)} {arg4,9}");
            Debug.WriteLine($"arg5 {new string(' ', 21)} {arg5,9}");
            Debug.WriteLine($"fileref {new string(' ', 17)} {fileref}");
            Debug.WriteLine($"ranges0 {new string(' ', 17)} {CombineIntArray(ranges0)}");
            Debug.WriteLine($"ranges1 {new string(' ', 17)} {CombineIntArray(ranges1)}");
            Debug.WriteLine($"ranges2 {new string(' ', 17)} {CombineIntArray(ranges2)}");
            Debug.WriteLine($"ranges3 {new string(' ', 17)} {ranges3[0]},{ranges3[1]},{ranges3[2]},{ranges3[3]}");
            Debug.WriteLine($"ranges4 {new string(' ', 17)} {ranges4[0]},{ranges4[1]},{ranges4[2]},{ranges4[3]}");
            Debug.WriteLine($"ranges5 {new string(' ', 17)} {ranges5[0]},{ranges5[1]},{ranges5[2]},{ranges5[3]}");
            Debug.WriteLine($"ranges6 {new string(' ', 17)} {CombineIntArray(ranges6)}");
            Debug.WriteLine($"ranges7 {new string(' ', 17)} {CombineIntArray(ranges7)}");
            Debug.WriteLine($"command0 {new string(' ', 16)} {command0}");
            Debug.WriteLine($"command1 {new string(' ', 16)} {command1}");
        }

        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            datareader.ShowByteCount($"PARAM-BLOCK[{blockId}]");
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
            uint paramType = datareader.ReadUIntAtPosition();
            datareader.OutputWriteLine($"// param-type, 6 or 7 lead dynamic-exp. Known values: 0,1,5,6,7,8,10,11,13");
            datareader.ShowBytes(4);
            if (paramType == 6 || paramType == 7)
            {
                int dynLength = datareader.ReadIntAtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment("dyn-exp len", 1);
                datareader.ShowBytes(dynLength, breakLine: false);
                datareader.TabComment("dynamic expression", 1);
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
            int a0 = datareader.ReadIntAtPosition(0);
            int a1 = datareader.ReadIntAtPosition(4);
            int a2 = datareader.ReadIntAtPosition(8);
            int a3 = datareader.ReadIntAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({FInt(a0)},{FInt(a1)},{FInt(a2)},{FInt(a3)})", 10);
            a0 = datareader.ReadIntAtPosition(0);
            a1 = datareader.ReadIntAtPosition(4);
            a2 = datareader.ReadIntAtPosition(8);
            a3 = datareader.ReadIntAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({FInt(a0)},{FInt(a1)},{FInt(a2)},{FInt(a3)})", 10);
            a0 = datareader.ReadIntAtPosition(0);
            a1 = datareader.ReadIntAtPosition(4);
            a2 = datareader.ReadIntAtPosition(8);
            a3 = datareader.ReadIntAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({FInt(a0)},{FInt(a1)},{FInt(a2)},{FInt(a3)})", 10);
            float f0 = datareader.ReadFloatAtPosition(0);
            float f1 = datareader.ReadFloatAtPosition(4);
            float f2 = datareader.ReadFloatAtPosition(8);
            float f3 = datareader.ReadFloatAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({FFlt(f0)},{FFlt(f1)},{FFlt(f2)},{FFlt(f3)})", 10);
            f0 = datareader.ReadFloatAtPosition(0);
            f1 = datareader.ReadFloatAtPosition(4);
            f2 = datareader.ReadFloatAtPosition(8);
            f3 = datareader.ReadFloatAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({FFlt(f0)},{FFlt(f1)},{FFlt(f2)},{FFlt(f3)})", 10);
            f0 = datareader.ReadFloatAtPosition(0);
            f1 = datareader.ReadFloatAtPosition(4);
            f2 = datareader.ReadFloatAtPosition(8);
            f3 = datareader.ReadFloatAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"floats ({FFlt(f0)},{FFlt(f1)},{FFlt(f2)},{FFlt(f3)})", 10);
            a0 = datareader.ReadIntAtPosition(0);
            a1 = datareader.ReadIntAtPosition(4);
            a2 = datareader.ReadIntAtPosition(8);
            a3 = datareader.ReadIntAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({FInt(a0)},{FInt(a1)},{FInt(a2)},{FInt(a3)})", 10);
            a0 = datareader.ReadIntAtPosition(0);
            a1 = datareader.ReadIntAtPosition(4);
            a2 = datareader.ReadIntAtPosition(8);
            a3 = datareader.ReadIntAtPosition(12);
            datareader.ShowBytes(16, breakLine: false);
            datareader.TabComment($"ints   ({FInt(a0)},{FInt(a1)},{FInt(a2)},{FInt(a3)})", 10);
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
            datareader.BreakLine();
        }
        private static string FFlt(float val)
        {
            if (val == -1e9) return "-inf";
            if (val == 1e9) return "inf";
            return $"{val}";
        }
        private static string FInt(int val)
        {
            if (val == -999999999) return "-inf";
            if (val == 999999999) return "inf";
            return "" + val; ;
        }
    }

    public class MipmapBlock : ShaderDataBlock
    {
        public int blockIndex;
        public MipmapBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            // TODO need to collect values
            datareader.MoveOffset(280);
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
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
        public int blockIndex;
        public string name;
        public int bufferSize;
        public List<(string, int, int, int, int)> bufferParams = new();
        public uint blockId;

        public BufferBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            name = datareader.ReadNullTermStringAtPosition();
            datareader.MoveOffset(64);
            bufferSize = datareader.ReadInt();
            datareader.MoveOffset(4); // these 4 bytes are always 0
            int paramCount = datareader.ReadInt();
            for (int i = 0; i < paramCount; i++)
            {
                string paramName = datareader.ReadNullTermStringAtPosition();
                datareader.MoveOffset(64);
                int bufferIndex = datareader.ReadInt();
                int arg0 = datareader.ReadInt();
                int arg1 = datareader.ReadInt();
                int arg2 = datareader.ReadInt();
                bufferParams.Add((paramName, bufferIndex, arg0, arg1, arg2));
            }
            blockId = datareader.ReadUInt();
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            string blockname = datareader.ReadNullTermStringAtPosition();
            datareader.ShowByteCount($"BUFFER-BLOCK[{blockIndex}] {blockname}");
            datareader.ShowBytes(64);
            uint bufferSize = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{bufferSize} buffer-size");
            datareader.ShowBytes(4);
            uint paramCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{paramCount} param-count");
            for (int i = 0; i < paramCount; i++) {
                string paramname = datareader.ReadNullTermStringAtPosition();
                datareader.OutputWriteLine($"// {paramname}");
                datareader.ShowBytes(64);
                uint paramIndex = datareader.ReadUIntAtPosition();
                datareader.ShowBytes(4, breakLine: false);
                datareader.TabComment($"{paramIndex} buffer-offset", 28);
                uint vertexSize = datareader.ReadUIntAtPosition();
                uint attributeCount = datareader.ReadUIntAtPosition(4);
                uint size = datareader.ReadUIntAtPosition(8);
                datareader.ShowBytes(12, $"({vertexSize},{attributeCount},{size}) (vertex-size, attribute-count, length)");
            }
            datareader.BreakLine();
            datareader.ShowBytes(4, "bufferID (some kind of crc/check)");
            datareader.BreakLine();
            datareader.BreakLine();
        }
    }

    public class SymbolsBlock : ShaderDataBlock
    {
        public int blockIndex;
        public List<(string, string, string, int)> symbolParams = new();

        public SymbolsBlock(ShaderDataReader datareader, long start, int blockIndex) : base(datareader, start)
        {
            this.blockIndex = blockIndex;
            int namesCount = datareader.ReadInt();
            for (int i = 0; i < namesCount; i++)
            {
                string name0 = datareader.ReadNullTermString();
                string name1 = datareader.ReadNullTermString();
                string name2 = datareader.ReadNullTermString();
                int int0 = datareader.ReadInt();
                symbolParams.Add((name0, name1, name2, int0));
            }
        }
        public void PrintAnnotatedBytestream()
        {
            datareader.SetPosition(start);
            datareader.ShowByteCount($"SYMBOL-NAMES-BLOCK[{blockIndex}]");
            uint symbolGroupCount = datareader.ReadUIntAtPosition();
            datareader.ShowBytes(4, $"{symbolGroupCount} string groups in this block");
            for (int i = 0; i < symbolGroupCount; i++) {
                for (int j = 0; j < 3; j++) {
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

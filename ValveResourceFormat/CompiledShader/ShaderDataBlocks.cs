using System;
using System.Collections.Generic;
using ValveResourceFormat.Utils;

namespace ValveResourceFormat.CompiledShader
{
    public class FeaturesHeaderBlock : ShaderDataBlock
    {
        public int VcsFileVersion { get; }
        public VcsAdditionalFiles AdditionalFiles { get; }
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
        public List<(string Name, string Shader, string StaticConfig)> Modes { get; } = new();
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

            if (VcsFileVersion >= 64)
            {
                AdditionalFiles = (VcsAdditionalFiles)datareader.ReadInt32();
            }

            if (!Enum.IsDefined(AdditionalFiles))
            {
                throw new UnexpectedMagicException("Unexpected v64 value", (int)AdditionalFiles, nameof(AdditionalFiles));
            }
            else if (AdditionalFiles == VcsAdditionalFiles.Rtx) // sbox
            {
                datareader.BaseStream.Position += 4;
                AdditionalFiles = VcsAdditionalFiles.None;
                VcsFileVersion = 64;
            }

            Version = datareader.ReadInt32();
            datareader.BaseStream.Position += 4; // length of name, but not needed because it's always null-term
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

            if (AdditionalFiles == VcsAdditionalFiles.Psrs)
            {
                datareader.BaseStream.Position += 4;
            }
            else if (AdditionalFiles == VcsAdditionalFiles.Rtx)
            {
                datareader.BaseStream.Position += 8;
            }

            var mode_count = datareader.ReadInt32();
            for (var i = 0; i < mode_count; i++)
            {
                var name = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                var shader = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;

                var static_config = string.Empty;
                if (datareader.ReadInt32() > 0)
                {
                    static_config = datareader.ReadNullTermStringAtPosition();
                    datareader.BaseStream.Position += 68;
                }
                Modes.Add((name, shader, static_config));
            }

            // Editor Id bytes, the length in-so-far is 16 bytes * (6 + AdditionalFiles + 1)
            for (var program = VcsProgramType.Features; program <= VcsProgramType.Undetermined; program++)
            {
                if (program == VcsProgramType.Undetermined)
                {
                    EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref - common editor reference shared by multiple files "));
                    continue;
                }

                if ((AdditionalFiles == VcsAdditionalFiles.None && program > VcsProgramType.ComputeShader)
                || (AdditionalFiles == VcsAdditionalFiles.Psrs && program > VcsProgramType.PixelShaderRenderState))
                {
                    continue;
                }

                EditorIDs.Add(($"{datareader.ReadBytesAsString(16)}", $"// Editor ref to {program}"));
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
                DataReader.ShowBytes(4, "has_psrs_file = " + (has_psrs_file > 0 ? "True" : "False"));
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

            var extraFile = VcsAdditionalFiles.None;
            if (VcsFileVersion >= 64)
            {
                extraFile = (VcsAdditionalFiles)datareader.ReadInt32();
                if (extraFile < VcsAdditionalFiles.None || extraFile > VcsAdditionalFiles.Rtx)
                {
                    throw new UnexpectedMagicException("unexpected v64 value", (int)extraFile, nameof(VcsAdditionalFiles));
                }
                if (extraFile == VcsAdditionalFiles.Rtx)
                {
                    datareader.BaseStream.Position += 4;
                    VcsFileVersion--;
                }
            }
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

    public interface ICombo
    {
        int BlockIndex { get; }
        string Name { get; }
        string Category { get; }
        int Arg0 { get; }
        int RangeMin { get; }
        int RangeMax { get; }
        int Arg3 { get; }
        int FeatureIndex { get; }
        int Arg5 { get; }

        void PrintByteDetail();
    }

    /// <summary>
    /// Contains a definition for a feature or static configuration.
    /// </summary>
    /// <remarks>
    /// These are usually 152 bytes long. Features may contain names describing each state
    /// </remarks>
    public class SfBlock : ShaderDataBlock, ICombo
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public string Category { get; }
        public int Arg0 { get; }
        public int RangeMin { get; }
        public int RangeMax { get; }
        public int Arg3 { get; }
        public int FeatureIndex { get; }
        public int Arg5 { get; }
        public List<string> CheckboxNames { get; } = new();
        public SfBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Category = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Arg0 = datareader.ReadInt32();
            RangeMin = datareader.ReadInt32();
            RangeMax = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            FeatureIndex = datareader.ReadInt32();
            Arg5 = datareader.ReadInt32AtPosition();
            var checkboxNameCount = datareader.ReadInt32();

            if (checkboxNameCount > 0 && RangeMax != checkboxNameCount - 1)
            {
                throw new InvalidOperationException("invalid");
            }

            for (var i = 0; i < checkboxNameCount; i++)
            {
                CheckboxNames.Add(datareader.ReadNullTermString());
            }

            // Seen in steampal's vr_complex VertexShader
            if (Arg3 == 11)
            {
                if (Name != "S_FOLIAGE_ANIMATION_ENABLED")
                {
                    throw new UnexpectedMagicException($"Unexpected static config with {nameof(Arg3)} = 11. Is it also 4 bytes longer?", Name, nameof(Name));
                }

                var foliage = datareader.ReadInt32();
                if (foliage != 0)
                {
                    throw new UnexpectedMagicException($"Unexpected additional arg", foliage, nameof(foliage));
                }
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

    // ConstraintBlocks are always 472 bytes long
    public class ConstraintBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public ConditionalRule Rule { get; }
        public ConditionalType BlockType { get; }
        public ConditionalType[] ConditionalTypes { get; }
        public int[] Indices { get; }
        public int[] Values { get; }
        public int[] Range2 { get; }
        public string Description { get; }
        public ConstraintBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Rule = (ConditionalRule)datareader.ReadInt32();
            BlockType = (ConditionalType)datareader.ReadInt32();
            ConditionalTypes = Array.ConvertAll(ReadByteFlags(), x => (ConditionalType)x);

            Indices = ReadIntRange();
            datareader.BaseStream.Position += 68 - Indices.Length * 4;
            Values = ReadIntRange();
            datareader.BaseStream.Position += 60 - Values.Length * 4;

            Range2 = ReadIntRange();
            datareader.BaseStream.Position += 64 - Range2.Length * 4;
            Description = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }

        public ConstraintBlock(ShaderDataReader datareader, int blockIndex, ConditionalType conditionalTypeVerify)
            : this(datareader, blockIndex)
        {
            UnexpectedMagicException.ThrowIfNotEqual(conditionalTypeVerify, BlockType, nameof(BlockType));
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
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"{BlockType}-CONTRAINTS-BLOCK[{BlockIndex}]");
            DataReader.ShowBytes(216);
            var name1 = DataReader.ReadNullTermStringAtPosition();
            DataReader.OutputWriteLine($"[{DataReader.BaseStream.Position}] {name1}");
            DataReader.ShowBytes(256);
            DataReader.BreakLine();
        }
    }

    // DBlocks are always 152 bytes long
    public class DBlock : ShaderDataBlock, ICombo
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public string Category { get; } // it looks like d-blocks might have the provision for a "category" (but not seen in use)
        public int Arg0 { get; }
        public int RangeMin { get; }
        public int RangeMax { get; }
        public int Arg3 { get; }
        public int FeatureIndex { get; }
        public int Arg5 { get; }
        public DBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Category = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Arg0 = datareader.ReadInt32();
            RangeMin = datareader.ReadInt32();
            RangeMax = datareader.ReadInt32();
            Arg3 = datareader.ReadInt32();
            FeatureIndex = datareader.ReadInt32();
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

    public class ParamBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public string UiGroup { get; }
        public string AttributeName { get; }
        public UiType UiType { get; }
        public float Res0 { get; }
        public int Lead0 { get; }
        public byte[] DynExp { get; } = Array.Empty<byte>();
        public int Arg0 { get; }
        public Vfx.Type VfxType { get; }
        public ParameterType ParamType { get; }
        public byte Arg3 { get; }
        public byte Arg4 { get; }
        public byte Arg5 { get; }
        public byte Arg6 { get; }
        public int VecSize { get; }
        public byte Id { get; }
        public byte Arg9 { get; }
        public byte Arg10 { get; }
        public byte Arg11 { get; }
        public string FileRef { get; }
        public static readonly float FloatInf = 1e9F;
        public static readonly int IntInf = 999999999;
        public int[] IntDefs { get; } = new int[4];
        public int[] IntMins { get; } = new int[4];
        public int[] IntMaxs { get; } = new int[4];
        public float[] FloatDefs { get; } = new float[4];
        public float[] FloatMins { get; } = new float[4];
        public float[] FloatMaxs { get; } = new float[4];
        public int ImageFormat { get; }
        public int ChannelCount { get; }
        public int[] ChannelIndices { get; } = new int[4];
        public int ColorMode { get; }
        public int Arg12 { get; }
        public string ImageSuffix { get; }
        public string ImageProcessor { get; }
        public byte[] V65Data { get; } = Array.Empty<byte>();
        public ParamBlock(ShaderDataReader datareader, int blockIndex, int vcsVersion) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            UiGroup = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            UiType = (UiType)datareader.ReadInt32();
            Res0 = datareader.ReadSingle();
            AttributeName = datareader.ReadNullTermStringAtPosition();
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

            VfxType = (Vfx.Type)datareader.ReadInt32();
            ParamType = (ParameterType)datareader.ReadInt32();

            Arg3 = datareader.ReadByte();
            Arg4 = datareader.ReadByte();
            Arg5 = datareader.ReadByte();
            Arg6 = datareader.ReadByte();

            VecSize = datareader.ReadInt32();
            if (vcsVersion > 62)
            {
                Id = datareader.ReadByte();
                Arg9 = datareader.ReadByte();
                Arg10 = datareader.ReadByte();
                Arg11 = datareader.ReadByte();
            }

            FileRef = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            for (var i = 0; i < 4; i++)
            {
                IntDefs[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                IntMins[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                IntMaxs[i] = datareader.ReadInt32();
            }
            for (var i = 0; i < 4; i++)
            {
                FloatDefs[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                FloatMins[i] = datareader.ReadSingle();
            }
            for (var i = 0; i < 4; i++)
            {
                FloatMaxs[i] = datareader.ReadSingle();
            }

            ImageFormat = datareader.ReadInt32();
            ChannelCount = datareader.ReadInt32();
            for (var i = 0; i < 4; i++)
            {
                ChannelIndices[i] = datareader.ReadInt32();
            }
            ColorMode = datareader.ReadInt32();
            Arg12 = datareader.ReadInt32();
            ImageSuffix = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 32;
            ImageProcessor = datareader.ReadNullTermStringAtPosition();
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
            if (val == -FloatInf)
            {
                return "-inf";
            }

            if (val == FloatInf)
            {
                return "inf";
            }

            return $"{val}";
        }
        private static string Fmt(int val)
        {
            if (val == -IntInf)
            {
                return "-inf";
            }

            if (val == IntInf)
            {
                return "inf";
            }

            return "" + val;
        }
    }

    // ChannelBlocks are always 280 bytes long
    public class ChannelBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }

        public Channel Channel { get; }
        public int[] InputTextureIndices { get; } = new int[4];
        public int ColorMode { get; }
        public string Name { get; }

        public ChannelBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Channel = (Channel)datareader.ReadUInt32();
            InputTextureIndices[0] = datareader.ReadInt32();
            InputTextureIndices[1] = datareader.ReadInt32();
            InputTextureIndices[2] = datareader.ReadInt32();
            InputTextureIndices[3] = datareader.ReadInt32();
            ColorMode = datareader.ReadInt32();
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
        public List<(string Name, int Offset, int VectorSize, int Depth, int Length)> BufferParams { get; } = new();
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
        public List<(string Name, string Type, string Option, int SemanticIndex)> SymbolsDefinition { get; } = new();

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

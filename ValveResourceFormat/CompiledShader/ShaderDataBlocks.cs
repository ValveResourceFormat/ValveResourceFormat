using System.Diagnostics;
using System.Text;
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
        public int FeaturesFileFlags { get; }
        public int VertexFileFlags { get; }
        public int PixelFileFlags { get; }
        public int GeometryFileFlags { get; }
        public int HullFileFlags { get; }
        public int DomainFileFlags { get; }
        public int ComputeFileFlags { get; }
        public int[] AdditionalFileFlags { get; }
        public List<(string Name, string Shader, string StaticConfig, int Value)> Modes { get; } = [];
        public List<(Guid, string)> EditorIDs { get; } = [];
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
            else if (datareader.IsSbox && AdditionalFiles == VcsAdditionalFiles.Rtx)
            {
                datareader.BaseStream.Position += 4;
                AdditionalFiles = VcsAdditionalFiles.None;
                VcsFileVersion = 64;
            }

            Version = datareader.ReadInt32();
            datareader.BaseStream.Position += 4; // length of name, but not needed because it's always null-term
            FileDescription = datareader.ReadNullTermString(Encoding.UTF8);
            DevShader = datareader.ReadInt32();

            FeaturesFileFlags = datareader.ReadInt32();
            VertexFileFlags = datareader.ReadInt32();
            PixelFileFlags = datareader.ReadInt32();
            GeometryFileFlags = datareader.ReadInt32();

            if (VcsFileVersion < 68)
            {
                HullFileFlags = datareader.ReadInt32();
                DomainFileFlags = datareader.ReadInt32();
            }

            if (VcsFileVersion >= 63)
            {
                ComputeFileFlags = datareader.ReadInt32();
            }

            AdditionalFileFlags = new int[(int)AdditionalFiles];
            for (var i = VcsAdditionalFiles.None; i < AdditionalFiles; i++)
            {
                AdditionalFileFlags[(int)i] = datareader.ReadInt32();
            };

            var modeCount = datareader.ReadInt32();

            for (var i = 0; i < modeCount; i++)
            {
                var name = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;
                var shader = datareader.ReadNullTermStringAtPosition();
                datareader.BaseStream.Position += 64;

                var static_config = string.Empty;
                var value = -1;
                if (datareader.ReadInt32() > 0)
                {
                    static_config = datareader.ReadNullTermStringAtPosition();
                    datareader.BaseStream.Position += 64;
                    value = datareader.ReadInt32();
                }
                Modes.Add((name, shader, static_config, value));
            }

            foreach (var programType in ProgramTypeIterator())
            {
                EditorIDs.Add((new Guid(datareader.ReadBytes(16)), $"// {programType}"));
            }

            EditorIDs.Add((new Guid(datareader.ReadBytes(16)), "// Common editor/compiler hash shared by multiple different vcs files."));
        }

        public IEnumerable<VcsProgramType> ProgramTypeIterator()
        {
            var programTypeLast = (int)VcsProgramType.ComputeShader + (int)AdditionalFiles;

            for (var i = 0; i <= programTypeLast; i++)
            {
                var programType = (VcsProgramType)i;

                // Version 63 adds compute shaders
                if (VcsFileVersion < 63 && programType is VcsProgramType.ComputeShader)
                {
                    continue;
                }

                // Version 68 removes hull and domain shaders
                if (VcsFileVersion >= 68 && programType is VcsProgramType.HullShader or VcsProgramType.DomainShader)
                {
                    continue;
                }

                yield return programType;
            }
        }

        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount("vcs file");
            DataReader.ShowBytes(4, "\"vcs2\"");
            DataReader.ShowBytes(4, $"{nameof(VcsFileVersion)} = {VcsFileVersion}");
            DataReader.BreakLine();
            DataReader.ShowByteCount("features header");
            if (VcsFileVersion >= 64)
            {
                DataReader.ShowBytes(4, $"{nameof(AdditionalFiles)} = {AdditionalFiles}");
            }
            DataReader.ShowBytes(4, $"{nameof(Version)} = {Version}");
            var len_name_description = DataReader.ReadInt32AtPosition();
            DataReader.ShowBytes(4, $"{len_name_description} len of name");
            DataReader.BreakLine();
            var name_desc = DataReader.ReadNullTermStringAtPosition();
            DataReader.ShowByteCount(name_desc);
            DataReader.ShowBytes(len_name_description + 1);
            DataReader.BreakLine();
            DataReader.ShowByteCount();
            DataReader.ShowBytes(4, $"DevShader bool");
            DataReader.ShowBytes(12, 4, breakLine: false);
            DataReader.TabComment($"({nameof(FeaturesFileFlags)}={FeaturesFileFlags},{nameof(VertexFileFlags)}={VertexFileFlags},{nameof(PixelFileFlags)}={PixelFileFlags})");

            var numArgs = VcsFileVersion < 64
                ? 3
                : VcsFileVersion < 68 ? 4 : 2;
            var dismissString = VcsFileVersion < 64
                ? nameof(ComputeFileFlags)
                : VcsFileVersion < 68 ? "none" : "hull & domain (v68)";
            DataReader.ShowBytes(numArgs * 4, 4, breakLine: false);
            DataReader.TabComment($"{nameof(GeometryFileFlags)}={GeometryFileFlags},{nameof(ComputeFileFlags)}={ComputeFileFlags},{nameof(HullFileFlags)}={HullFileFlags},{nameof(DomainFileFlags)}={DomainFileFlags}) dismissing: {dismissString}");

            DataReader.BreakLine();
            DataReader.ShowByteCount();

            for (var i = 0; i < (int)AdditionalFiles; i++)
            {
                DataReader.ShowBytes(4, $"arg8[{i}] = {AdditionalFileFlags[i]} (additional file {i})");
            }

            DataReader.ShowBytes(4, $"mode count = {Modes.Count}");
            DataReader.BreakLine();
            DataReader.ShowByteCount();
            foreach (var mode in Modes)
            {
                DataReader.Comment(mode.Name);
                DataReader.ShowBytes(64);
                DataReader.Comment(mode.Shader);
                DataReader.ShowBytes(64);
                DataReader.ShowBytes(4, "Has static config?");
                if (mode.StaticConfig.Length != 0)
                {
                    DataReader.Comment(mode.StaticConfig);
                    DataReader.ShowBytes(68);
                }
            }
            DataReader.BreakLine();
            DataReader.ShowByteCount("Editor/Shader stack for generating the file");
            foreach (var (guid, comment) in EditorIDs)
            {
                DataReader.ShowBytes(16, comment);
            }

            DataReader.BreakLine();
        }
    }

    public class VsPsHeaderBlock : ShaderDataBlock
    {
        public int VcsFileVersion { get; }
        public Guid FileID0 { get; }
        public Guid FileID1 { get; }
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
                if (datareader.IsSbox && extraFile == VcsAdditionalFiles.Rtx)
                {
                    datareader.BaseStream.Position += 4;
                    VcsFileVersion--;
                }
            }
            FileID0 = new Guid(datareader.ReadBytes(16));
            FileID1 = new Guid(datareader.ReadBytes(16));
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
            DataReader.ShowBytes(16, "Common editor/compiler hash shared by multiple different vcs files.");
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
        public int Arg3 { get; } // S_TOOLS_ENABLED = 1, S_SHADER_QUALITY = 2
        public int FeatureIndex { get; }
        public int Arg5 { get; }
        public List<string> CheckboxNames { get; } = [];
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
                CheckboxNames.Add(datareader.ReadNullTermString(Encoding.UTF8));
            }

            if (Arg3 == 11)
            {
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
            List<string> names = [];
            for (var i = 0; i < arg5; i++)
            {
                var paramname = DataReader.ReadNullTermString(Encoding.UTF8);
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
            if (BlockType != conditionalTypeVerify)
            {
                throw new UnexpectedMagicException($"Unexpected {nameof(BlockType)}", $"{BlockType}", nameof(BlockType));
            }
        }

        private int[] ReadIntRange()
        {
            List<int> ints0 = [];
            while (DataReader.ReadInt32AtPosition() >= 0)
            {
                ints0.Add(DataReader.ReadInt32());
            }
            return [.. ints0];
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

    [Flags]
    public enum LeadFlags
    {
        None = 0x00,
        Attribute = 0x01,
        Dynamic = 0x02,
        Expression = 0x04,
        DynMaterial = 0x08,
    }

    public class ParamBlock : ShaderDataBlock
    {
        public int BlockIndex { get; }
        public string Name { get; }
        public UiGroup UiGroup { get; }
        public string StringData { get; }
        public UiType UiType { get; }
        public float Res0 { get; }
        public LeadFlags Lead0 { get; }
        public byte[] DynExp { get; } = [];
        public byte[] UiVisibilityExp { get; } = [];
        public int Tex { get; }
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
        public byte[] V65Data { get; } = [];
        public ParamBlock(ShaderDataReader datareader, int blockIndex, int vcsVersion) : base(datareader)
        {
            BlockIndex = blockIndex;
            Name = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            UiGroup = UiGroup.FromCompactString(datareader.ReadNullTermStringAtPosition());
            datareader.BaseStream.Position += 64;
            UiType = (UiType)datareader.ReadInt32();
            Res0 = datareader.ReadSingle();
            StringData = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 64;
            Lead0 = (LeadFlags)datareader.ReadInt32();

            if (HasDynamicExpression)
            {
                var dynExpLen = datareader.ReadInt32();
                DynExp = datareader.ReadBytes(dynExpLen);
            }

            Tex = datareader.ReadInt32();

            // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
            if (Tex == 0x534D4253)
            {
                var dynExpLen = datareader.ReadInt32();
                UiVisibilityExp = datareader.ReadBytes(dynExpLen);

                Tex = datareader.ReadInt32();
            }

            VfxType = (Vfx.Type)datareader.ReadInt32();
            ParamType = (ParameterType)datareader.ReadInt32();

            if (vcsVersion > 63)
            {
                Arg3 = datareader.ReadByte();
                Arg4 = datareader.ReadByte();
                Arg5 = datareader.ReadByte();
                Arg6 = datareader.ReadByte();
            }

            VecSize = datareader.ReadInt32();

            Id = datareader.ReadByte();
            Arg9 = datareader.ReadByte();
            Arg10 = datareader.ReadByte();
            Arg11 = datareader.ReadByte();

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

            Debug.Assert(!float.IsNaN(FloatMaxs[3]));

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

        public bool HasDynamicExpression
            => Lead0.HasFlag(LeadFlags.Dynamic)
            && Lead0.HasFlag(LeadFlags.Expression)
            && !Lead0.HasFlag(LeadFlags.DynMaterial);

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

            if (vcsVersion >= 65)
            {
                DataReader.ShowBytes(6, "unknown bytes specific to vcs version >= 65");
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

        public ChannelMapping Channel { get; }
        public int[] InputTextureIndices { get; } = new int[4];
        public int ColorMode { get; }
        public string TexProcessorName { get; }

        public ChannelBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            Channel = (ChannelMapping)datareader.ReadUInt32();
            InputTextureIndices[0] = datareader.ReadInt32();
            InputTextureIndices[1] = datareader.ReadInt32();
            InputTextureIndices[2] = datareader.ReadInt32();
            InputTextureIndices[3] = datareader.ReadInt32();
            ColorMode = datareader.ReadInt32();
            TexProcessorName = datareader.ReadNullTermStringAtPosition();
            datareader.BaseStream.Position += 256;
        }
        public void PrintByteDetail()
        {
            DataReader.BaseStream.Position = Start;
            DataReader.ShowByteCount($"CHANNEL-BLOCK[{BlockIndex}]");
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
        public List<(string Name, int Offset, int VectorSize, int Depth, int Length)> BufferParams { get; } = [];
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
        public List<(string Name, string Type, string Option, int SemanticIndex)> SymbolsDefinition { get; } = [];

        public VertexSymbolsBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
        {
            BlockIndex = blockIndex;
            SymbolsCount = datareader.ReadInt32();
            for (var i = 0; i < SymbolsCount; i++)
            {
                var name = datareader.ReadNullTermString(Encoding.UTF8);
                var type = datareader.ReadNullTermString(Encoding.UTF8);
                var option = datareader.ReadNullTermString(Encoding.UTF8);
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

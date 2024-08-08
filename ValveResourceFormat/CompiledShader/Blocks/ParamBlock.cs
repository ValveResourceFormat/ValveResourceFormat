using System.Diagnostics;

namespace ValveResourceFormat.CompiledShader;

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

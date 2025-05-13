using System.Diagnostics;

namespace ValveResourceFormat.CompiledShader;

public class VfxVariableDescription : ShaderDataBlock
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
    public int Field1 { get; }
    public int VecSize { get; }
    public int Id { get; }
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
    public int Field2 { get; }
    public string ImageSuffix { get; }
    public string ImageProcessor { get; }
    public byte Field3 { get; }
    public bool Field4 { get; }
    public int Field5 { get; }

    public VfxVariableDescription(ShaderDataReader datareader, int blockIndex, int vcsVersion) : base(datareader)
    {
        // CVfxVariableDescription::Unserialize
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

        if (vcsVersion >= 64)
        {
            Field1 = datareader.ReadInt32();
        }

        VecSize = datareader.ReadInt32();
        Id = datareader.ReadInt32();

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
        Field2 = datareader.ReadInt32();

        ImageSuffix = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 32;
        ImageProcessor = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 32;

        if (vcsVersion >= 65)
        {
            Field3 = datareader.ReadByte();
            Field4 = datareader.ReadBoolean();
            Field5 = datareader.ReadInt32();
        }
    }

    public bool HasDynamicExpression
        => Lead0.HasFlag(LeadFlags.Dynamic)
        && Lead0.HasFlag(LeadFlags.Expression)
        && !Lead0.HasFlag(LeadFlags.DynMaterial);
}

using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

public class VfxVariableDescription : ShaderDataBlock
{
    public int BlockIndex { get; }
    public string Name { get; }
    public UiGroup UiGroup { get; }
    public string StringData { get; }
    public UiType UiType { get; }
    public float UiStep { get; }
    public VfxVariableSourceType VariableSource { get; }
    public byte[] DynExp { get; } = [];
    public byte[] UiVisibilityExp { get; } = [];
    public int SourceIndex { get; }
    public VfxVariableType VfxType { get; }
    public VfxRegisterType RegisterType { get; }
    public VariableFlags Flags => (VariableFlags)((ContextStateAffectedByVariable >> 8) & 0xFF);
    public int ContextStateAffectedByVariable { get; }
    public uint RegisterOffset { get; }
    public uint DescriptorSet { get; }
    public int RegisterElements { get; }
    public bool SrgbRead => (ExtConstantBufferId & 0x01) == 1;
    public int ExtConstantBufferId { get; }
    public string DefaultInputTexture { get; }
    public static readonly float FloatInf = 1e9F;
    public static readonly int IntInf = 1000000000;
    public int[] IntDefs { get; } = new int[4];
    public int[] IntMins { get; } = [-IntInf, -IntInf, -IntInf, -IntInf];
    public int[] IntMaxs { get; } = [IntInf, IntInf, IntInf, IntInf];
    public float[] FloatDefs { get; } = new float[4];
    public float[] FloatMins { get; } = [-FloatInf, -FloatInf, -FloatInf, -FloatInf];
    public float[] FloatMaxs { get; } = [FloatInf, FloatInf, FloatInf, FloatInf];
    public ImageFormat ImageFormat { get; } = ImageFormat.UNKNOWN;
    public int ChannelCount { get; }
    public int[] ChannelIndices { get; } = [-1, -1, -1, -1];
    public int ColorMode { get; }
    public int MinPrecisionBits { get; } = -1;
    public string ImageSuffix { get; }
    public string ImageProcessor { get; }
    public byte LayerId { get; }
    public bool AllowLayerOverride { get; }
    public int MaxRes { get; }
    public bool IsLayerConstant { get; }

    public VfxVariableDescription(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;
        Name = data.GetProperty<string>("m_szName");
        UiGroup = UiGroup.FromCompactString(data.GetProperty<string>("m_szUiGroup"));
        UiType = (UiType)data.GetInt32Property("m_uiType");
        UiStep = data.GetFloatProperty("m_flUiStep");
        StringData = data.GetProperty<string>("m_pSourceString");
        VariableSource = (VfxVariableSourceType)data.GetInt32Property("m_sourceType");

        if (data.GetProperty<byte[]>("m_pCompiledExpression") is byte[] compiledExpression)
        {
            DynExp = compiledExpression;
        }

        UiVisibilityExp = data.GetProperty<byte[]>("m_pCompiledUIVisibilityExpression");

        SourceIndex = data.GetInt32Property("m_sourceIndex");
        VfxType = (VfxVariableType)data.GetInt32Property("m_type");
        RegisterType = (VfxRegisterType)data.GetInt32Property("m_registerType");
        ContextStateAffectedByVariable = data.GetInt32Property("m_nContextStateAffectedByVariable");

        RegisterOffset = data.GetUInt32Property("m_nRegisterOffset");
        DescriptorSet = data.GetUInt32Property("m_nDescriptorSet");

        RegisterElements = data.GetInt32Property("m_nRegisterElements");
        ExtConstantBufferId = unchecked((int)data.GetUInt32Property("m_nTypeSpecificBits"));

        if (data.ContainsKey("m_flDefault"))
        {
            FloatDefs = data.GetFloatArray("m_flDefault");
            FloatMins = data.GetFloatArray("m_flMin");
            FloatMaxs = data.GetFloatArray("m_flMax");

            IntMins = [.. FloatMins.Select(fl => (int)MathF.Floor(fl))];
            IntMaxs = [.. FloatMaxs.Select(fl => (int)MathF.Floor(fl))];
            IntDefs = [.. FloatDefs.Select(fl => (int)MathF.Floor(fl))];
        }
        else if (data.ContainsKey("m_intDefault"))
        {
            IntDefs = [.. data.GetIntegerArray("m_intDefault").Select(l => (int)l)];
            IntMins = [.. data.GetIntegerArray("m_intMin").Select(l => (int)l)];
            IntMaxs = [.. data.GetIntegerArray("m_intMax").Select(l => (int)l)];

            FloatMins = [.. IntMins.Select(i => (float)i)];
            FloatMaxs = [.. IntMaxs.Select(i => (float)i)];
            FloatDefs = [.. IntDefs.Select(i => (float)i)];
        }

        FixupIntMinsMaxs();

        // Texture properties, not always present
        // todo: better detection
        if (data.ContainsKey("m_outputTextureFormat"))
        {
            DefaultInputTexture = data.GetProperty<string>("m_defaultInputTexture");
            ImageFormat = (ImageFormat)data.GetUInt32Property("m_outputTextureFormat");
            ChannelCount = data.GetInt32Property("m_nChannelCount");
            ChannelIndices = data.GetArray<int>("m_nChannelInfoIndex");
            ColorMode = data.GetInt32Property("m_inputColorSpace");
            MinPrecisionBits = data.GetInt32Property("m_nMinPrecisionBits");

            ImageSuffix = data.GetProperty<string>("m_szTextureFileEnding");
            ImageProcessor = data.GetProperty<string>("m_inputProcessingCommand");
            MaxRes = data.GetInt32Property("m_nMaxRes");
        }
        else
        {
            DefaultInputTexture = string.Empty;
            ImageSuffix = string.Empty;
            ImageProcessor = string.Empty;
        }

        LayerId = (byte)data.GetInt32Property("m_nLayerId");
        AllowLayerOverride = data.GetProperty<bool>("m_bAllowLayerOverride");
        IsLayerConstant = data.GetProperty<bool>("m_bIsLayerConstant");
    }

    public VfxVariableDescription(BinaryReader datareader, int blockIndex, int vcsVersion) : base(datareader)
    {
        // CVfxVariableDescription::Unserialize
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);
        UiGroup = UiGroup.FromCompactString(ReadStringWithMaxLength(datareader, 64));
        UiType = (UiType)datareader.ReadInt32();
        UiStep = datareader.ReadSingle();
        StringData = ReadStringWithMaxLength(datareader, 64);
        VariableSource = (VfxVariableSourceType)datareader.ReadInt32();

        if (HasDynamicExpression)
        {
            var dynExpLen = datareader.ReadInt32();
            DynExp = datareader.ReadBytes(dynExpLen);
        }

        SourceIndex = datareader.ReadInt32();

        // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
        if (SourceIndex == 0x534D4253)
        {
            var dynExpLen = datareader.ReadInt32();
            UiVisibilityExp = datareader.ReadBytes(dynExpLen);

            SourceIndex = datareader.ReadInt32();
        }

        VfxType = (VfxVariableType)datareader.ReadInt32();
        RegisterType = (VfxRegisterType)datareader.ReadInt32();

        if (vcsVersion >= 64)
        {
            ContextStateAffectedByVariable = datareader.ReadInt32();
        }

        RegisterElements = datareader.ReadInt32();
        ExtConstantBufferId = datareader.ReadInt32();

        DefaultInputTexture = ReadStringWithMaxLength(datareader, 64);

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

        FixupIntMinsMaxs();

        Debug.Assert(!float.IsNaN(FloatMaxs[3]));

        ImageFormat = (ImageFormat)datareader.ReadInt32();
        ChannelCount = datareader.ReadInt32();
        for (var i = 0; i < 4; i++)
        {
            ChannelIndices[i] = datareader.ReadInt32();
        }

        ColorMode = datareader.ReadInt32();
        MinPrecisionBits = datareader.ReadInt32();

        ImageSuffix = ReadStringWithMaxLength(datareader, 32);
        ImageProcessor = ReadStringWithMaxLength(datareader, 32);

        if (vcsVersion >= 65)
        {
            LayerId = datareader.ReadByte();
            AllowLayerOverride = datareader.ReadBoolean();
            MaxRes = datareader.ReadInt32();
        }

        if (vcsVersion >= 69)
        {
            IsLayerConstant = datareader.ReadBoolean();
        }
    }

    public bool HasDynamicExpression
        => VariableSource is VfxVariableSourceType.__Expression__
                          or VfxVariableSourceType.__SetByArtistAndExpression__;

    private void FixupIntMinsMaxs()
    {
        const int OldIntInf = 999999999;
        for (var i = 0; i < 4; i++)
        {
            if (IntMins[i] == -OldIntInf)
            {
                IntMins[i] = -IntInf;
            }

            if (IntMaxs[i] == OldIntInf)
            {
                IntMaxs[i] = IntInf;
            }
        }
    }
}

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
    public int Tex { get; }
    public VfxVariableType VfxType { get; }
    public VfxRegisterType RegisterType { get; }
    public int Field1 { get; }
    public VariableFlags Flags => (VariableFlags)((Field1 >> 8) & 0xFF);
    public int VecSize { get; }
    public int ExtConstantBufferId { get; }
    public string FileRef { get; }
    public static readonly float FloatInf = 1e9F;
    public static readonly int IntInf = 999999999;
    public int[] IntDefs { get; } = new int[4];
    public int[] IntMins { get; } = [-IntInf, -IntInf, -IntInf, -IntInf];
    public int[] IntMaxs { get; } = [IntInf, IntInf, IntInf, IntInf];
    public float[] FloatDefs { get; } = new float[4];
    public float[] FloatMins { get; } = [-FloatInf, -FloatInf, -FloatInf, -FloatInf];
    public float[] FloatMaxs { get; } = [FloatInf, FloatInf, FloatInf, FloatInf];
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
    public byte Field6 { get; }

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

        Tex = data.GetInt32Property("m_sourceIndex");
        VfxType = (VfxVariableType)data.GetInt32Property("m_type");
        RegisterType = (VfxRegisterType)data.GetInt32Property("m_registerType");

        // Uncertain about these
        // todo
        Field1 = data.GetInt32Property("m_nContextStateAffectedByVariable");

        data.GetUInt32Property("m_nRegisterOffset"); // todo
        data.GetUInt32Property("m_nDescriptorSet"); // todo
        data.GetUInt32Property("m_nTypeSpecificBits"); // todo

        VecSize = data.GetInt32Property("m_nRegisterElements");

        // ExtConstantBufferId
        // FileRef

        if (data.ContainsKey("m_flDefault"))
        {
            FloatDefs = data.GetFloatArray("m_flDefault");
            FloatMins = data.GetFloatArray("m_flMin");
            FloatMaxs = data.GetFloatArray("m_flMax");
        }
        else if (data.ContainsKey("m_intDefault"))
        {
            IntDefs = data.GetIntegerArray("m_intDefault").Select(l => (int)l).ToArray();
            IntMins = data.GetIntegerArray("m_intMin").Select(l => (int)l).ToArray();
            IntMaxs = data.GetIntegerArray("m_intMax").Select(l => (int)l).ToArray();
        }

        Field5 = data.GetInt32Property("m_nLayerId");

        // todo: verify these two
        Field4 = data.GetProperty<bool>("m_bAllowLayerOverride");
        Field6 = data.GetProperty<bool>("m_bIsLayerConstant") ? (byte)1 : (byte)0;

        // Texture properties, not always present
        // todo: better detection
        if (data.ContainsKey("m_outputTextureFormat"))
        {
            FileRef = data.GetProperty<string>("m_defaultInputTexture");
            ImageFormat = unchecked((int)data.GetUInt32Property("m_outputTextureFormat"));
            ChannelCount = data.GetInt32Property("m_nChannelCount");
            ChannelIndices = data.GetArray<int>("m_nChannelInfoIndex");
            ColorMode = data.GetInt32Property("m_inputColorSpace");
            data.GetInt32Property("m_nMinPrecisionBits"); // todo

            ImageSuffix = data.GetProperty<string>("m_szTextureFileEnding");
            ImageProcessor = data.GetProperty<string>("m_inputProcessingCommand");
            data.GetInt32Property("m_nMaxRes"); // todo
        }
        else
        {
            FileRef = string.Empty;
            ImageSuffix = string.Empty;
            ImageProcessor = string.Empty;
        }
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

        Tex = datareader.ReadInt32();

        // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
        if (Tex == 0x534D4253)
        {
            var dynExpLen = datareader.ReadInt32();
            UiVisibilityExp = datareader.ReadBytes(dynExpLen);

            Tex = datareader.ReadInt32();
        }

        VfxType = (VfxVariableType)datareader.ReadInt32();
        RegisterType = (VfxRegisterType)datareader.ReadInt32();

        if (vcsVersion >= 64)
        {
            Field1 = datareader.ReadInt32();
        }

        VecSize = datareader.ReadInt32();
        ExtConstantBufferId = datareader.ReadInt32();

        FileRef = ReadStringWithMaxLength(datareader, 64);

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

        ImageSuffix = ReadStringWithMaxLength(datareader, 32);
        ImageProcessor = ReadStringWithMaxLength(datareader, 32);

        if (vcsVersion >= 65)
        {
            Field3 = datareader.ReadByte();
            Field4 = datareader.ReadBoolean();
            Field5 = datareader.ReadInt32();
        }

        if (vcsVersion >= 69)
        {
            Field6 = datareader.ReadByte();
        }
    }

    public bool HasDynamicExpression => VariableSource is VfxVariableSourceType.__Expression__ or VfxVariableSourceType.__SetByArtistAndExpression__;
}

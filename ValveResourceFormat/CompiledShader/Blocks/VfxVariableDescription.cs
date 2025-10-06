using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Represents a variable description in a VFX shader program.
/// </summary>
public class VfxVariableDescription : ShaderDataBlock
{
    /// <summary>Gets the block index of this variable.</summary>
    public int BlockIndex { get; }

    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Gets the UI group for this variable.</summary>
    public UiGroup UiGroup { get; }

    /// <summary>Gets the string data associated with this variable.</summary>
    public string StringData { get; }

    /// <summary>Gets the UI type for this variable.</summary>
    public UiType UiType { get; }

    /// <summary>Gets the UI step value for this variable.</summary>
    public float UiStep { get; }

    /// <summary>Gets the variable source type.</summary>
    public VfxVariableSourceType VariableSource { get; }

    /// <summary>Gets the compiled dynamic expression bytecode.</summary>
    public byte[] DynExp { get; } = [];

    /// <summary>Gets the compiled UI visibility expression bytecode.</summary>
    public byte[] UiVisibilityExp { get; } = [];

    /// <summary>Gets the source index for this variable.</summary>
    public int SourceIndex { get; }

    /// <summary>Gets the VFX variable type.</summary>
    public VfxVariableType VfxType { get; }

    /// <summary>Gets the register type for this variable.</summary>
    public VfxRegisterType RegisterType { get; }

    /// <summary>Gets the variable flags.</summary>
    public VariableFlags Flags => (VariableFlags)((ContextStateAffectedByVariable >> 8) & 0xFF);

    /// <summary>Gets the context state affected by this variable.</summary>
    public int ContextStateAffectedByVariable { get; }

    /// <summary>Gets the register offset.</summary>
    public uint RegisterOffset { get; }

    /// <summary>Gets the descriptor set index.</summary>
    public uint DescriptorSet { get; }

    /// <summary>Gets the number of register elements.</summary>
    public int RegisterElements { get; }

    /// <summary>Gets a value indicating whether sRGB reads are disabled for this variable.</summary>
    public bool SrgbRead => (ExtConstantBufferId & 0x01) == 1;

    /// <summary>Gets the external constant buffer ID.</summary>
    public int ExtConstantBufferId { get; }

    /// <summary>Gets the default input texture name.</summary>
    public string DefaultInputTexture { get; }

    /// <summary>Float infinity value used for min/max ranges.</summary>
    public static readonly float FloatInf = 1e9F;

    /// <summary>Integer infinity value used for min/max ranges.</summary>
    public static readonly int IntInf = 1000000000;

    /// <summary>Gets the integer default values (up to 4 components).</summary>
    public int[] IntDefs { get; } = new int[4];

    /// <summary>Gets the integer minimum values (up to 4 components).</summary>
    public int[] IntMins { get; } = [-IntInf, -IntInf, -IntInf, -IntInf];

    /// <summary>Gets the integer maximum values (up to 4 components).</summary>
    public int[] IntMaxs { get; } = [IntInf, IntInf, IntInf, IntInf];

    /// <summary>Gets the float default values (up to 4 components).</summary>
    public float[] FloatDefs { get; } = new float[4];

    /// <summary>Gets the float minimum values (up to 4 components).</summary>
    public float[] FloatMins { get; } = [-FloatInf, -FloatInf, -FloatInf, -FloatInf];

    /// <summary>Gets the float maximum values (up to 4 components).</summary>
    public float[] FloatMaxs { get; } = [FloatInf, FloatInf, FloatInf, FloatInf];

    /// <summary>Gets the image format for texture variables.</summary>
    public ImageFormat ImageFormat { get; } = ImageFormat.UNKNOWN;

    /// <summary>Gets the number of texture channels.</summary>
    public int ChannelCount { get; }

    /// <summary>Gets the channel indices for texture variables.</summary>
    public int[] ChannelIndices { get; } = [-1, -1, -1, -1];

    /// <summary>Gets the color mode for texture variables.</summary>
    public int ColorMode { get; }

    /// <summary>Gets the minimum precision bits required for this variable.</summary>
    public int MinPrecisionBits { get; } = -1;

    /// <summary>Gets the image file suffix for texture variables.</summary>
    public string ImageSuffix { get; }

    /// <summary>Gets the image processing command for texture variables.</summary>
    public string ImageProcessor { get; }

    /// <summary>Gets the layer ID for this variable.</summary>
    public byte LayerId { get; }

    /// <summary>Gets whether layer override is allowed.</summary>
    public bool AllowLayerOverride { get; }

    /// <summary>Gets the maximum resolution for texture variables.</summary>
    public int MaxRes { get; }

    /// <summary>Gets whether this is a layer constant.</summary>
    public bool IsLayerConstant { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableDescription"/> class from KeyValues data.
    /// </summary>
    /// <param name="data">The KeyValues object containing variable data.</param>
    /// <param name="blockIndex">The block index for this variable.</param>
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

            if (RegisterType is VfxRegisterType.Uniform)
            {
                IntMins = [.. FloatMins.Select(fl => (int)MathF.Floor(fl))];
                IntMaxs = [.. FloatMaxs.Select(fl => (int)MathF.Floor(fl))];
                IntDefs = [.. FloatDefs.Select(fl => (int)MathF.Floor(fl))];
            }
        }
        else if (data.ContainsKey("m_intDefault"))
        {
            IntDefs = [.. data.GetIntegerArray("m_intDefault").Select(l => (int)l)];
            IntMins = [.. data.GetIntegerArray("m_intMin").Select(l => (int)l)];
            IntMaxs = [.. data.GetIntegerArray("m_intMax").Select(l => (int)l)];

            if (RegisterType is VfxRegisterType.Uniform)
            {
                FloatMins = [.. IntMins.Select(i => (float)i)];
                FloatMaxs = [.. IntMaxs.Select(i => (float)i)];
                FloatDefs = [.. IntDefs.Select(i => (float)i)];
            }
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

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableDescription"/> class from a binary stream.
    /// </summary>
    /// <param name="datareader">The binary reader to read from.</param>
    /// <param name="blockIndex">The block index for this variable.</param>
    /// <param name="vcsVersion">The VCS file version.</param>
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

    /// <summary>
    /// Gets whether this variable has a dynamic expression.
    /// </summary>
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

using System.Diagnostics;
using System.IO;
using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Represents a variable description in a VFX shader program.
/// </summary>
public class VfxVariableDescription : ShaderDataBlock
{
    /// <summary>Gets the block index of this variable.</summary>
    public int Index { get; }

    /// <summary>Gets the variable name.</summary>
    public string Name { get; }

    /// <summary>Gets the UI group for this variable.</summary>
    public UiGroup UiGroup { get; }

    /// <summary>Gets the source string for this variable.</summary>
    public string SourceString { get; }

    /// <summary>Gets the UI type for this variable.</summary>
    public UiType UiType { get; }

    /// <summary>Gets the UI step value for this variable.</summary>
    public float UiStep { get; }

    /// <summary>Gets the variable source type.</summary>
    public VfxVariableSourceType VariableSource { get; }

    /// <summary>Gets the compiled dynamic expression bytecode.</summary>
    public byte[] CompiledExpression { get; } = [];

    /// <summary>Gets the compiled UI visibility expression bytecode.</summary>
    public byte[] UiVisibilityExpression { get; } = [];

    /// <summary>Gets the source index for this variable.</summary>
    public int SourceIndex { get; }

    /// <summary>Gets the VFX variable type.</summary>
    public VfxVariableType VfxType { get; }

    /// <summary>Gets the register type for this variable.</summary>
    public VfxRegisterType RegisterType { get; }

    /// <summary>Gets the variable flags, stored in bits 8-15 of <see cref="ContextStateAffectedByVariable"/>.</summary>
    public VariableFlags Flags => (VariableFlags)((ContextStateAffectedByVariable >> 8) & 0xFF);

    /// <summary>
    /// Gets the context state affected by this variable. Only stored since version 64,
    /// so a zero value on older files means unknown rather than no affected state.
    /// </summary>
    public int ContextStateAffectedByVariable { get; }

    /// <summary>Gets the register offset. Only stored in KV3 resources; 0 for binary vcs files.</summary>
    public uint RegisterOffset { get; }

    /// <summary>Gets the descriptor set index. Only stored in KV3 resources; 0 for binary vcs files.</summary>
    public uint DescriptorSet { get; }

    /// <summary>Gets the number of register elements.</summary>
    public int RegisterElements { get; }

    /// <summary>Gets a value indicating whether hardware sRGB reads are enabled for this variable.</summary>
    public bool SrgbRead => (TypeSpecificBits & 0x01) == 1;

    /// <summary>
    /// Gets bits whose meaning depends on the variable type, such as the external constant buffer ID.
    /// It is -1 for variables that carry no type specific data.
    /// </summary>
    public int TypeSpecificBits { get; }

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

    /// <summary>Gets the output texture format for texture variables.</summary>
    public ImageFormat OutputTextureFormat { get; } = ImageFormat.UNKNOWN;

    /// <summary>Gets the number of valid entries in <see cref="ChannelInfoIndices"/>.</summary>
    public int ChannelCount { get; }

    /// <summary>Gets the indices into <see cref="VfxProgramData.TextureChannelProcessors"/> for texture variables.</summary>
    public int[] ChannelInfoIndices { get; } = [-1, -1, -1, -1];

    /// <summary>Gets the input color space for texture variables.</summary>
    public int InputColorSpace { get; }

    /// <summary>Gets the minimum precision bits required for this variable.</summary>
    public int MinPrecisionBits { get; } = -1;

    /// <summary>Gets the texture file name suffix for texture variables.</summary>
    public string TextureFileEnding { get; }

    /// <summary>Gets the input processing command for texture variables.</summary>
    public string InputProcessingCommand { get; }

    /// <summary>Gets the layer ID for this variable.</summary>
    public byte LayerId { get; }

    /// <summary>Gets whether layer override is allowed.</summary>
    public bool AllowLayerOverride { get; }

    /// <summary>Gets the maximum resolution for texture variables.</summary>
    public int MaxRes { get; }

    /// <summary>Gets whether this is a layer constant.</summary>
    public bool IsLayerConstant { get; }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableDescription"/> class from <see cref="KVObject"/> data.
    /// </summary>
    /// <param name="data">The <see cref="KVObject"/> containing variable data.</param>
    /// <param name="index">The index in the owning array.</param>
    public VfxVariableDescription(KVObject data, int index) : base()
    {
        Index = index;
        Name = RegisterName(data.GetStringProperty("m_szName"));
        UiGroup = UiGroup.FromCompactString(data.GetStringProperty("m_szUiGroup"));
        UiType = (UiType)data.GetInt32Property("m_uiType");
        UiStep = data.GetFloatProperty("m_flUiStep");
        SourceString = data.GetStringProperty("m_pSourceString");
        VariableSource = (VfxVariableSourceType)data.GetInt32Property("m_sourceType");

        if (data.GetArray<byte>("m_pCompiledExpression") is byte[] compiledExpression)
        {
            CompiledExpression = compiledExpression;
        }

        UiVisibilityExpression = data.GetArray<byte>("m_pCompiledUIVisibilityExpression") ?? [];

        SourceIndex = data.GetInt32Property("m_sourceIndex");
        VfxType = (VfxVariableType)data.GetInt32Property("m_type");
        RegisterType = (VfxRegisterType)data.GetInt32Property("m_registerType");
        ContextStateAffectedByVariable = data.GetInt32Property("m_nContextStateAffectedByVariable");

        RegisterOffset = data.GetUInt32Property("m_nRegisterOffset");
        DescriptorSet = data.GetUInt32Property("m_nDescriptorSet");

        RegisterElements = data.GetInt32Property("m_nRegisterElements");
        TypeSpecificBits = unchecked((int)data.GetUInt32Property("m_nTypeSpecificBits"));

        if (data.ContainsKey("m_flDefault"))
        {
            FloatDefs = data.GetFloatArray("m_flDefault");
            FloatMins = data.GetFloatArray("m_flMin");
            FloatMaxs = data.GetFloatArray("m_flMax");

            if (RegisterType is VfxRegisterType.Float4)
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

            if (RegisterType is VfxRegisterType.Float4)
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
            DefaultInputTexture = data.GetStringProperty("m_defaultInputTexture");
            OutputTextureFormat = (ImageFormat)data.GetUInt32Property("m_outputTextureFormat");
            ChannelCount = data.GetInt32Property("m_nChannelCount");
            ChannelInfoIndices = data.GetArray<int>("m_nChannelInfoIndex")!;
            InputColorSpace = data.GetInt32Property("m_inputColorSpace");
            MinPrecisionBits = data.GetInt32Property("m_nMinPrecisionBits");

            TextureFileEnding = data.GetStringProperty("m_szTextureFileEnding");
            InputProcessingCommand = data.GetStringProperty("m_inputProcessingCommand");
            MaxRes = data.GetInt32Property("m_nMaxRes");
        }
        else
        {
            DefaultInputTexture = string.Empty;
            TextureFileEnding = string.Empty;
            InputProcessingCommand = string.Empty;
        }

        LayerId = (byte)data.GetInt32Property("m_nLayerId");
        AllowLayerOverride = data.GetBooleanProperty("m_bAllowLayerOverride");
        IsLayerConstant = data.GetBooleanProperty("m_bIsLayerConstant");
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableDescription"/> class from a binary stream.
    /// </summary>
    /// <param name="datareader">The binary reader to read from.</param>
    /// <param name="index">The index in the owning array.</param>
    /// <param name="vcsVersion">The VCS file version.</param>
    public VfxVariableDescription(BinaryReader datareader, int index, int vcsVersion) : base(datareader)
    {
        // CVfxVariableDescription::Unserialize
        Index = index;
        Name = RegisterName(ReadStringWithMaxLength(datareader, 64));
        UiGroup = UiGroup.FromCompactString(ReadStringWithMaxLength(datareader, 64));
        UiType = (UiType)datareader.ReadInt32();
        UiStep = datareader.ReadSingle();
        SourceString = ReadStringWithMaxLength(datareader, 64);
        VariableSource = (VfxVariableSourceType)datareader.ReadInt32();

        if (HasDynamicExpression)
        {
            var dynExpLen = datareader.ReadInt32();
            CompiledExpression = datareader.ReadBytes(dynExpLen);
        }

        SourceIndex = datareader.ReadInt32();

        // check to see if this reads 'SBMS' (unknown what this is, instance found in v65 hero_pc_40_features.vcs file)
        if (SourceIndex == 0x534D4253)
        {
            var dynExpLen = datareader.ReadInt32();
            UiVisibilityExpression = datareader.ReadBytes(dynExpLen);

            SourceIndex = datareader.ReadInt32();
        }

        VfxType = (VfxVariableType)datareader.ReadInt32();
        RegisterType = (VfxRegisterType)datareader.ReadInt32();

        if (vcsVersion >= 64)
        {
            ContextStateAffectedByVariable = datareader.ReadInt32();
        }

        RegisterElements = datareader.ReadInt32();
        TypeSpecificBits = datareader.ReadInt32();

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

        OutputTextureFormat = (ImageFormat)datareader.ReadInt32();
        ChannelCount = datareader.ReadInt32();
        for (var i = 0; i < 4; i++)
        {
            ChannelInfoIndices[i] = datareader.ReadInt32();
        }

        InputColorSpace = datareader.ReadInt32();
        MinPrecisionBits = datareader.ReadInt32();

        TextureFileEnding = ReadStringWithMaxLength(datareader, 32);
        InputProcessingCommand = ReadStringWithMaxLength(datareader, 32);

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

    /// <summary>
    /// Type prefixes used in shader variable names, longest first.
    /// </summary>
    public static IReadOnlyList<string> TypePrefixes { get; } = ["g_fl", "g_f", "g_v", "g_n", "g_b", "g_t"];

    // Dynamic expressions refer to variables by the hash of their name, and some of them hash
    // the name without the type prefix it is declared with.
    private static string RegisterName(string name)
    {
        StringToken.Store(name);

        foreach (var prefix in TypePrefixes)
        {
            if (name.Length > prefix.Length && name.StartsWith(prefix, StringComparison.Ordinal))
            {
                StringToken.Store(name.AsSpan(prefix.Length));
                break;
            }
        }

        return name;
    }

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

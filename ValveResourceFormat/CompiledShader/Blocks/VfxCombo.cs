using System.IO;
using System.Text;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains a definition for a shader combo, whether static, dynamic, or feature-specific.
/// </summary>
/// <remarks>
/// Features may contain names describing each state.
/// </remarks>
public class VfxCombo : ShaderDataBlock
{
    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }
    /// <summary>Gets or sets the offset this combo contributes to a combo id per state step.</summary>
    public long ComboIndexValue { get; set; } // set after loading all combos
    /// <summary>Gets the combo name.</summary>
    public string Name { get; }
    /// <summary>Gets the alias name.</summary>
    public string AliasName { get; }
    /// <summary>Gets the combo type (static or dynamic).</summary>
    public VfxComboType ComboType { get; }
    /// <summary>Gets the minimum value in the combo range.</summary>
    public int RangeMin { get; }
    /// <summary>Gets the maximum value in the combo range.</summary>
    public int RangeMax { get; }
    /// <summary>Gets the combo source type.</summary>
    public int ComboSourceType { get; } // VfxStaticComboSourceType or VfxDynamicComboSourceType
    /// <summary>Gets the feature comparison value.</summary>
    public int FeatureComparisonValue { get; }
    /// <summary>Gets the feature index.</summary>
    public int FeatureIndex { get; }
    /// <summary>Gets the display name of each state.</summary>
    public string[] StateNames { get; } = [];

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VfxCombo(KVObject data, int index, int vcsVersion) : base()
    {
        Index = index;
        Name = data.GetStringProperty("m_szName");
        AliasName = data.GetStringProperty("m_szAliasName") ?? string.Empty;
        ComboType = data.GetEnumValue<VfxComboType>("m_comboType", normalize: true, stripExtension: "Type");
        RangeMin = data.GetInt32Property("m_nMin");
        RangeMax = data.GetInt32Property("m_nMax");
        ComboSourceType = NormalizeComboSourceType(data.GetInt32Property("m_shaderComboSourceType"), vcsVersion);
        FeatureIndex = data.GetInt32Property("m_iFeatureIndex");
        StateNames = data.GetArray<string>("m_stringArray")!;

        ComboIndexValue = data.GetIntegerProperty("m_nComboIndexValue");

        if (ComboSourceType is ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_EQ__) or ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_NE__))
        {
            FeatureComparisonValue = data.GetInt32Property("m_nCompareValue");
        }
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxCombo(BinaryReader datareader, int index, int vcsVersion) : base(datareader)
    {
        // CVfxCombo::Unserialize
        Index = index;
        Name = ReadStringWithMaxLength(datareader, 64);
        AliasName = ReadStringWithMaxLength(datareader, 64);
        ComboType = (VfxComboType)datareader.ReadInt32();
        RangeMin = datareader.ReadInt32();
        RangeMax = datareader.ReadInt32();
        ComboSourceType = NormalizeComboSourceType(datareader.ReadInt32(), vcsVersion);
        FeatureIndex = datareader.ReadInt32();

        var stateNameCount = datareader.ReadInt32();

        if (stateNameCount > 0)
        {
            StateNames = new string[stateNameCount];

            for (var i = 0; i < stateNameCount; i++)
            {
                StateNames[i] = datareader.ReadNullTermString(Encoding.UTF8);
            }
        }

        if (ComboSourceType is ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_EQ__) or ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_NE__))
        {
            FeatureComparisonValue = datareader.ReadInt32();
        }
    }

    private int NormalizeComboSourceType(int comboSourceType, int vcsVersion)
    {
        if (vcsVersion >= 71 && ComboType == VfxComboType.Static && comboSourceType >= (int)VfxStaticComboSourceType.S_BINDLESS_RUNTIME)
        {
            comboSourceType += 2;
        }

        return comboSourceType;
    }
}

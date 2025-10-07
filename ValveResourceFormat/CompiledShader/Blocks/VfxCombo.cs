using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains a definition for a shader combo, whether static, dynamic, or feature-specific.
/// </summary>
/// <remarks>
/// These are usually 152 bytes long. Features may contain names describing each state
/// </remarks>
public class VfxCombo : ShaderDataBlock
{
    /// <summary>Gets the block index.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets or sets the calculated combo identifier.</summary>
    public long CalculatedComboId { get; set; } // set after loading all combos
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
    /// <summary>Gets the array of state names.</summary>
    public string[] Strings { get; } = [];

    /// <summary>
    /// Initializes a new instance from KeyValues data.
    /// </summary>
    public VfxCombo(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;
        Name = data.GetProperty<string>("m_szName");
        AliasName = data.GetProperty<string>("m_szAliasName") ?? string.Empty;
        ComboType = data.GetEnumValue<VfxComboType>("m_comboType", normalize: true, stripExtension: "Type");
        RangeMin = data.GetInt32Property("m_nMin");
        RangeMax = data.GetInt32Property("m_nMax");
        ComboSourceType = data.GetInt32Property("m_shaderComboSourceType");
        FeatureIndex = data.GetInt32Property("m_iFeatureIndex");
        Strings = data.GetArray<string>("m_stringArray");

        CalculatedComboId = data.GetIntegerProperty("m_nComboIndexValue");

        if (ComboSourceType is ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_EQ__) or ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_NE__))
        {
            FeatureComparisonValue = data.GetInt32Property("m_nCompareValue");
        }
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VfxCombo(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxCombo::Unserialize
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);
        AliasName = ReadStringWithMaxLength(datareader, 64);
        ComboType = (VfxComboType)datareader.ReadInt32();
        RangeMin = datareader.ReadInt32();
        RangeMax = datareader.ReadInt32();
        ComboSourceType = datareader.ReadInt32();
        FeatureIndex = datareader.ReadInt32();

        var stringsCount = datareader.ReadInt32();

        if (stringsCount > 0)
        {
            Strings = new string[stringsCount];

            for (var i = 0; i < stringsCount; i++)
            {
                Strings[i] = datareader.ReadNullTermString(Encoding.UTF8);
            }
        }

        if (ComboSourceType is ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_EQ__) or ((int)VfxStaticComboSourceType.__SET_BY_FEATURE_NE__))
        {
            FeatureComparisonValue = datareader.ReadInt32();
        }
    }
}

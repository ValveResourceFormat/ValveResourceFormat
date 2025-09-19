using System.IO;
using System.Linq;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains a definition for a feature or static configuration.
/// </summary>
/// <remarks>
/// These are usually 152 bytes long. Features may contain names describing each state
/// </remarks>
public class VfxCombo : ShaderDataBlock
{
    public int BlockIndex { get; }
    public long CalculatedComboId { get; set; } // set after loading all combos
    public string Name { get; }
    public string Category { get; }
    public VfxComboType ComboType { get; }
    public int RangeMin { get; }
    public int RangeMax { get; }
    public int ComboSourceType { get; } // VfxStaticComboSourceType or VfxDynamicComboSourceType
    public int FeatureComparisonValue { get; }
    public int FeatureIndex { get; }
    public string[] Strings { get; } = [];

    public VfxCombo(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;
        Name = data.GetProperty<string>("m_szName");
        Category = data.GetProperty<string>("m_szCategory");
        ComboType = data.GetEnumValue<VfxComboType>("m_comboType", normalize: true, stripExtension: "Type");
        RangeMin = data.GetInt32Property("m_nMin");
        RangeMax = data.GetInt32Property("m_nMax");
        ComboSourceType = data.GetInt32Property("m_shaderComboSourceType");
        FeatureIndex = data.GetInt32Property("m_iFeatureIndex");
        Strings = data.GetArray<string>("m_stringArray");

        CalculatedComboId = data.GetIntegerProperty("m_nComboIndexValue");

        // todo: verify this
        // FeatureComparisonValue = data.ContainsKey("m_nCompareValue") ? data.GetInt32Property("m_nCompareValue") : 0;
    }

    public VfxCombo(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxCombo::Unserialize
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);
        Category = ReadStringWithMaxLength(datareader, 64);
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

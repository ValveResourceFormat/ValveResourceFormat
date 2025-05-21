using System.IO;
using System.Text;

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
    public int ComboType { get; } // 1 - static, 2 - dynamic
    public int RangeMin { get; }
    public int RangeMax { get; }
    public int ComboSourceType { get; } // VfxStaticComboSourceType or VfxDynamicComboSourceType
    public int FeatureIndex { get; }
    public List<string> CheckboxNames { get; } = [];

    public VfxCombo(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxCombo::Unserialize
        BlockIndex = blockIndex;
        Name = ReadStringWithMaxLength(datareader, 64);
        Category = ReadStringWithMaxLength(datareader, 64);
        ComboType = datareader.ReadInt32();
        RangeMin = datareader.ReadInt32();
        RangeMax = datareader.ReadInt32();
        ComboSourceType = datareader.ReadInt32();
        FeatureIndex = datareader.ReadInt32();
        var checkboxNameCount = datareader.ReadInt32();

        if (checkboxNameCount > 0 && RangeMax != checkboxNameCount - 1)
        {
            throw new InvalidOperationException("invalid");
        }

        for (var i = 0; i < checkboxNameCount; i++)
        {
            CheckboxNames.Add(datareader.ReadNullTermString(Encoding.UTF8));
        }

        // TODO: This seems wrong
        if (ComboSourceType == (int)VfxStaticComboSourceType.S_EXECUTION_REORDERING || ComboSourceType == (int)VfxStaticComboSourceType.__SET_BY_FEATURE_NE__)
        {
            var value = datareader.ReadInt32();
            if (value != 0)
            {
                throw new UnexpectedMagicException($"Unexpected additional arg", value, nameof(value));
            }
        }
    }
}

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
    public string Name { get; }
    public string Category { get; }
    public int Arg0 { get; }
    public int RangeMin { get; }
    public int RangeMax { get; }
    public int Arg3 { get; } // S_TOOLS_ENABLED = 1, S_SHADER_QUALITY = 2
    public int FeatureIndex { get; }
    public List<string> CheckboxNames { get; } = [];

    public VfxCombo(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        // CVfxCombo::Unserialize
        BlockIndex = blockIndex;
        Name = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 64;
        Category = datareader.ReadNullTermStringAtPosition();
        datareader.BaseStream.Position += 64;
        Arg0 = datareader.ReadInt32();
        RangeMin = datareader.ReadInt32();
        RangeMax = datareader.ReadInt32();
        Arg3 = datareader.ReadInt32();
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

        if (Arg3 == 10 || Arg3 == 11)
        {
            var foliage = datareader.ReadInt32();
            if (foliage != 0)
            {
                throw new UnexpectedMagicException($"Unexpected additional arg", foliage, nameof(foliage));
            }
        }
    }
}

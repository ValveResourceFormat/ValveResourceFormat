using System.Text;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Contains a definition for a feature or static configuration.
/// </summary>
/// <remarks>
/// These are usually 152 bytes long. Features may contain names describing each state
/// </remarks>
public class SfBlock : ShaderDataBlock, ICombo
{
    public int BlockIndex { get; }
    public string Name { get; }
    public string Category { get; }
    public int Arg0 { get; }
    public int RangeMin { get; }
    public int RangeMax { get; }
    public int Arg3 { get; } // S_TOOLS_ENABLED = 1, S_SHADER_QUALITY = 2
    public int FeatureIndex { get; }
    public int Arg5 { get; }
    public List<string> CheckboxNames { get; } = [];

    public SfBlock(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
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
        Arg5 = datareader.ReadInt32AtPosition();
        var checkboxNameCount = datareader.ReadInt32();

        if (checkboxNameCount > 0 && RangeMax != checkboxNameCount - 1)
        {
            throw new InvalidOperationException("invalid");
        }

        for (var i = 0; i < checkboxNameCount; i++)
        {
            CheckboxNames.Add(datareader.ReadNullTermString(Encoding.UTF8));
        }

        if (Arg3 == 11)
        {
            var foliage = datareader.ReadInt32();
            if (foliage != 0)
            {
                throw new UnexpectedMagicException($"Unexpected additional arg", foliage, nameof(foliage));
            }
        }
    }

    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowByteCount();
        for (var i = 0; i < 2; i++)
        {
            var name1 = DataReader.ReadNullTermStringAtPosition();
            if (name1.Length > 0)
            {
                DataReader.Comment($"{name1}");
            }
            DataReader.ShowBytes(64);
        }
        var arg0 = DataReader.ReadInt32AtPosition(0);
        var arg1 = DataReader.ReadInt32AtPosition(4);
        var arg2 = DataReader.ReadInt32AtPosition(8);
        var arg3 = DataReader.ReadInt32AtPosition(12);
        var arg4 = DataReader.ReadInt32AtPosition(16);
        var arg5 = DataReader.ReadInt32AtPosition(20);
        DataReader.ShowBytes(16, 4, breakLine: false);
        DataReader.TabComment($"({arg0},{arg1},{arg2},{arg3})");
        DataReader.ShowBytes(4, $"({arg4}) known values [-1,28]");
        DataReader.ShowBytes(4, $"{arg5} additional string params");
        var string_offset = (int)DataReader.BaseStream.Position;
        List<string> names = [];
        for (var i = 0; i < arg5; i++)
        {
            var paramname = DataReader.ReadNullTermString(Encoding.UTF8);
            names.Add(paramname);
            string_offset += paramname.Length + 1;
        }
        if (names.Count > 0)
        {
            PrintStringList(names);
            DataReader.ShowBytes(string_offset - (int)DataReader.BaseStream.Position);
        }
        DataReader.BreakLine();
    }

    private void PrintStringList(List<string> names)
    {
        if (names.Count == 0)
        {
            return;
        }
        DataReader.OutputWrite($"// {names[0]}");
        for (var i = 1; i < names.Count; i++)
        {
            DataReader.OutputWrite($", {names[i]}");
        }
        DataReader.BreakLine();
    }
}

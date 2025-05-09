using System.Text;

namespace ValveResourceFormat.CompiledShader;

public class VsInputSignatureElement : ShaderDataBlock
{
    public int BlockIndex { get; }
    public int SymbolsCount { get; }
    public List<(string Name, string Type, string Option, int SemanticIndex)> SymbolsDefinition { get; } = [];

    public VsInputSignatureElement(ShaderDataReader datareader, int blockIndex) : base(datareader)
    {
        // VfxUnserializeVsInputSignature
        BlockIndex = blockIndex;
        SymbolsCount = datareader.ReadInt32();
        for (var i = 0; i < SymbolsCount; i++)
        {
            var name = datareader.ReadNullTermString(Encoding.UTF8);
            var type = datareader.ReadNullTermString(Encoding.UTF8);
            var option = datareader.ReadNullTermString(Encoding.UTF8);
            var semanticIndex = datareader.ReadInt32();
            SymbolsDefinition.Add((name, type, option, semanticIndex));
        }
    }
    public void PrintByteDetail()
    {
        DataReader.BaseStream.Position = Start;
        DataReader.ShowByteCount($"SYMBOL-NAMES-BLOCK[{BlockIndex}]");
        var symbolGroupCount = DataReader.ReadUInt32AtPosition();
        DataReader.ShowBytes(4, $"{symbolGroupCount} string groups in this block");
        for (var i = 0; i < symbolGroupCount; i++)
        {
            for (var j = 0; j < 3; j++)
            {
                var symbolname = DataReader.ReadNullTermStringAtPosition();
                DataReader.OutputWriteLine($"// {symbolname}");
                DataReader.ShowBytes(symbolname.Length + 1);
            }
            DataReader.ShowBytes(4);
            DataReader.BreakLine();
        }
        if (symbolGroupCount == 0)
        {
            DataReader.BreakLine();
        }
    }
}

using System.IO;
using System.Text;

namespace ValveResourceFormat.CompiledShader;

public class VsInputSignatureElement : ShaderDataBlock
{
    public int BlockIndex { get; }
    public int SymbolsCount { get; }
    public List<(string Name, string Type, string Option, int SemanticIndex)> SymbolsDefinition { get; } = [];

    public VsInputSignatureElement(BinaryReader datareader, int blockIndex) : base(datareader)
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
}

using System.IO;
using System.Text;
using static ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.CompiledShader;

public class VsInputSignatureElement : ShaderDataBlock
{
    public int BlockIndex { get; }
    public InputSignatureElement[] SymbolsDefinition { get; } = [];

    public VsInputSignatureElement(BinaryReader datareader, int blockIndex) : base(datareader)
    {
        // VfxUnserializeVsInputSignature
        BlockIndex = blockIndex;

        var symbolsCount = datareader.ReadInt32();
        SymbolsDefinition = new InputSignatureElement[symbolsCount];
        for (var i = 0; i < symbolsCount; i++)
        {
            var name = datareader.ReadNullTermString(Encoding.UTF8);
            var type = datareader.ReadNullTermString(Encoding.UTF8);
            var option = datareader.ReadNullTermString(Encoding.UTF8);
            var semanticIndex = datareader.ReadInt32();
            SymbolsDefinition[i] = new(name, type, option, semanticIndex);
        }
    }
}

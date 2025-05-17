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
            var d3dSemantic = datareader.ReadNullTermString(Encoding.UTF8);
            var semantic = datareader.ReadNullTermString(Encoding.UTF8);
            var d3dSemanticIndex = datareader.ReadInt32();
            SymbolsDefinition[i] = new(name, semantic, d3dSemantic, d3dSemanticIndex);
        }
    }
}

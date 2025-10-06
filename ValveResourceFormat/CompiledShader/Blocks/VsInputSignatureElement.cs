using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Text;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Vertex shader input signature definitions.
/// </summary>
public class VsInputSignatureElement : ShaderDataBlock
{
    /// <summary>Gets the block index.</summary>
    public int BlockIndex { get; }
    /// <summary>Gets the array of input signature elements.</summary>
    public InputSignatureElement[] SymbolsDefinition { get; } = [];

    /// <summary>
    /// Initializes a new instance from KeyValues data.
    /// </summary>
    public VsInputSignatureElement(KVObject data, int blockIndex) : base()
    {
        BlockIndex = blockIndex;

        Debug.Assert(data.IsArray);
        SymbolsDefinition = new InputSignatureElement[data.Count];

        for (var i = 0; i < data.Count; i++)
        {
            var definition = (KVObject)data[i].Value!;
            SymbolsDefinition[i] = new(definition);
        }
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
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

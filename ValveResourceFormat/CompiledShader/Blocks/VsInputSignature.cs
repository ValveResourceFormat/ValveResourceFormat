using System.Diagnostics;
using System.IO;
using System.Text;
using ValveKeyValue;
using static ValveResourceFormat.ResourceTypes.Material;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Vertex shader input signature.
/// </summary>
/// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/VsInputSignatureElement_t">VsInputSignatureElement_t</seealso>
public class VsInputSignature : ShaderDataBlock
{
    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }
    /// <summary>Gets the array of input signature elements.</summary>
    public InputSignatureElement[] Elements { get; } = [];

    /// <summary>
    /// Initializes a new instance from <see cref="KVObject"/> data.
    /// </summary>
    public VsInputSignature(KVObject data, int index) : base()
    {
        Index = index;

        Debug.Assert(data.IsArray);
        Elements = new InputSignatureElement[data.Count];

        for (var i = 0; i < data.Count; i++)
        {
            var definition = data[i];
            Elements[i] = new(definition);
        }
    }

    /// <summary>
    /// Initializes a new instance from a binary reader.
    /// </summary>
    public VsInputSignature(BinaryReader datareader, int index) : base(datareader)
    {
        // VfxUnserializeVsInputSignature
        Index = index;

        var elementCount = datareader.ReadInt32();
        Elements = new InputSignatureElement[elementCount];
        for (var i = 0; i < elementCount; i++)
        {
            var name = datareader.ReadNullTermString(Encoding.UTF8);
            var d3dSemantic = datareader.ReadNullTermString(Encoding.UTF8);
            var semantic = datareader.ReadNullTermString(Encoding.UTF8);
            var d3dSemanticIndex = datareader.ReadInt32();
            Elements[i] = new(name, semantic, d3dSemantic, d3dSemanticIndex);
        }
    }
}

using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// A variable write sequence: the ordered list of variable indices and register offsets a combo
/// writes its variables with, split into evaluated, render state, and constant segments.
/// </summary>
public class VfxVariableIndexArray : ShaderDataBlock
{
    /// <summary>Gets the index in the owning array.</summary>
    public int Index { get; }

    /// <summary>Gets the index of the first render state element.</summary>
    public int FirstRenderStateElement { get; }

    /// <summary>Gets the index of the first constant element.</summary>
    public int FirstConstantElement { get; }

    /// <summary>Gets the array of variable index fields.</summary>
    public VfxVariableIndexData[] Fields { get; }

    /// <summary>Gets the evaluated variable indices.</summary>
    public IReadOnlyList<VfxVariableIndexData> Evaluated => Fields[..FirstRenderStateElement];

    /// <summary>Gets the render state variable indices.</summary>
    public IReadOnlyList<VfxVariableIndexData> RenderState => Fields[FirstRenderStateElement..FirstConstantElement];

    /// <summary>Gets the constant variable indices.</summary>
    public IReadOnlyList<VfxVariableIndexData> Constants => Fields[FirstConstantElement..];

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableIndexArray"/> class from a span of fields.
    /// </summary>
    /// <param name="fields">The variable index fields.</param>
    /// <param name="firstRenderStateElement">The index of the first render state element.</param>
    /// <param name="firstConstantElement">The index of the first constant element.</param>
    /// <param name="index">The index in the owning array.</param>
    public VfxVariableIndexArray(ReadOnlySpan<uint> fields, int firstRenderStateElement, int firstConstantElement, int index) : base()
    {
        Index = index;
        Fields = MemoryMarshal.Cast<uint, VfxVariableIndexData>(fields).ToArray();
        FirstRenderStateElement = firstRenderStateElement;
        FirstConstantElement = firstConstantElement;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableIndexArray"/> class from a binary stream.
    /// </summary>
    /// <param name="datareader">The binary reader to read from.</param>
    /// <param name="index">The index in the owning array.</param>
    /// <param name="readRegisterOffset">Whether to read the register offset.</param>
    public VfxVariableIndexArray(BinaryReader datareader, int index, bool readRegisterOffset) : base(datareader)
    {
        Index = index;
        var fieldCount = datareader.ReadInt32();
        FirstRenderStateElement = datareader.ReadInt32();
        FirstConstantElement = datareader.ReadInt32();

        Fields = new VfxVariableIndexData[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            if (readRegisterOffset)
            {
                Fields[i] = new VfxVariableIndexData
                {
                    PackedIndex = datareader.ReadInt16(),
                    RegisterOffset = datareader.ReadInt16(),
                };
            }
            else
            {
                Fields[i] = new VfxVariableIndexData
                {
                    PackedIndex = datareader.ReadInt16(),
                    RegisterOffset = 0,
                };
            }
        }
    }
}

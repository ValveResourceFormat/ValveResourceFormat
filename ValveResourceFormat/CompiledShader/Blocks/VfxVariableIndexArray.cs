using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

/// <summary>
/// Represents an array of variable indices in a VFX shader program.
/// </summary>
public class VfxVariableIndexArray : ShaderDataBlock
{
    /// <summary>Gets the block ID.</summary>
    public int BlockId { get; }

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

    /// <summary>Gets the global variable indices.</summary>
    public IReadOnlyList<VfxVariableIndexData> Globals => Fields[FirstConstantElement..];

    // TODO: remove this
    /// <summary>
    /// Gets the raw data as a byte span.
    /// </summary>
    public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<VfxVariableIndexData>(Fields);

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableIndexArray"/> class from a span of fields.
    /// </summary>
    /// <param name="fields">The variable index fields.</param>
    /// <param name="firstRenderStateElement">The index of the first render state element.</param>
    /// <param name="firstConstantElement">The index of the first constant element.</param>
    /// <param name="blockIndex">The block index.</param>
    public VfxVariableIndexArray(ReadOnlySpan<uint> fields, int firstRenderStateElement, int firstConstantElement, int blockIndex) : base()
    {
        BlockId = blockIndex;
        Fields = MemoryMarshal.Cast<uint, VfxVariableIndexData>(fields).ToArray();
        FirstRenderStateElement = firstRenderStateElement;
        FirstConstantElement = firstConstantElement;
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="VfxVariableIndexArray"/> class from a binary stream.
    /// </summary>
    /// <param name="datareader">The binary reader to read from.</param>
    /// <param name="blockId">The block ID.</param>
    /// <param name="readDest">Whether to read the destination field.</param>
    public VfxVariableIndexArray(BinaryReader datareader, int blockId, bool readDest) : base(datareader)
    {
        BlockId = blockId;
        var fieldCount = datareader.ReadInt32();
        FirstRenderStateElement = datareader.ReadInt32();
        FirstConstantElement = datareader.ReadInt32();

        Fields = new VfxVariableIndexData[fieldCount];
        for (var i = 0; i < fieldCount; i++)
        {
            if (readDest)
            {
                Fields[i] = new VfxVariableIndexData
                {
                    Field1 = datareader.ReadInt16(),
                    Field2 = datareader.ReadInt16(),
                };
            }
            else
            {
                Fields[i] = new VfxVariableIndexData
                {
                    Field1 = datareader.ReadInt16(),
                    Field2 = 0,
                };
            }
        }
    }
}

using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

public class VfxVariableIndexArray : ShaderDataBlock
{
    public int BlockId { get; }
    public int FirstRenderStateElement { get; }
    public int FirstConstantElement { get; }

    public VfxVariableIndexData[] Fields { get; }
    public IReadOnlyList<VfxVariableIndexData> Evaluated => Fields[..FirstRenderStateElement];
    public IReadOnlyList<VfxVariableIndexData> RenderState => Fields[FirstRenderStateElement..FirstConstantElement];
    public IReadOnlyList<VfxVariableIndexData> Globals => Fields[FirstConstantElement..];

    // TODO: remove this
    public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<VfxVariableIndexData>(Fields);

    public VfxVariableIndexArray(ReadOnlySpan<uint> fields, int firstRenderStateElement, int firstConstantElement, int blockIndex) : base()
    {
        BlockId = blockIndex;
        Fields = MemoryMarshal.Cast<uint, VfxVariableIndexData>(fields).ToArray();
        FirstRenderStateElement = firstRenderStateElement;
        FirstConstantElement = firstConstantElement;
    }

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

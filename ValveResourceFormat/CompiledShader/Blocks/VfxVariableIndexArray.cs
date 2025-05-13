using System.IO;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

public class VfxVariableIndexArray : ShaderDataBlock
{
    public int BlockId { get; }
    public int FieldsCount { get; }
    public int Offset1 { get; }
    public int Offset2 { get; }

    public VfxVariableIndexData[] Fields { get; }
    public IReadOnlyList<VfxVariableIndexData> Evaluated => Fields[..Offset1];
    public IReadOnlyList<VfxVariableIndexData> Segment1 => Fields[Offset1..Offset2];
    public IReadOnlyList<VfxVariableIndexData> Globals => Fields[Offset2..];

    // TODO: remove this
    public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<VfxVariableIndexData>(Fields);

    public VfxVariableIndexArray(BinaryReader datareader, int blockId, bool readDest) : base(datareader)
    {
        BlockId = blockId;
        FieldsCount = datareader.ReadInt32();
        Offset1 = datareader.ReadInt32();
        Offset2 = datareader.ReadInt32();

        Fields = new VfxVariableIndexData[FieldsCount];
        for (var i = 0; i < FieldsCount; i++)
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

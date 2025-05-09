using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

public class VfxVariableIndexArray : ShaderDataBlock
{
    public int BlockId { get; }
    public int H0 { get; }
    public int H1 { get; }
    public int H2 { get; }

    public VfxVariableIndexData[] Fields { get; }
    public IReadOnlyList<VfxVariableIndexData> Evaluated => Fields[..H1];
    public IReadOnlyList<VfxVariableIndexData> Segment1 => Fields[H1..H2];
    public IReadOnlyList<VfxVariableIndexData> Globals => Fields[H2..];
    public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<VfxVariableIndexData>(Fields);

    public VfxVariableIndexArray(ShaderDataReader datareader, int blockId, bool readDest) : base(datareader)
    {
        BlockId = blockId;
        H0 = datareader.ReadInt32();
        H1 = datareader.ReadInt32();
        H2 = datareader.ReadInt32();

        Fields = new VfxVariableIndexData[H0];
        for (var i = 0; i < H0; i++)
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

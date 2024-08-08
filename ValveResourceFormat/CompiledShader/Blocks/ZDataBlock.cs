using System.Runtime.InteropServices;

namespace ValveResourceFormat.CompiledShader;

public class ZDataBlock : ShaderDataBlock
{
    public int BlockId { get; }
    public int H0 { get; }
    public int H1 { get; }
    public int H2 { get; }

    public WriteSeqField[] Fields { get; }
    public IReadOnlyList<WriteSeqField> Evaluated => Fields[..H1];
    public IReadOnlyList<WriteSeqField> Segment1 => Fields[H1..H2];
    public IReadOnlyList<WriteSeqField> Globals => Fields[H2..];
    public ReadOnlySpan<byte> Dataload => MemoryMarshal.AsBytes<WriteSeqField>(Fields);

    public ZDataBlock(ShaderDataReader datareader, int blockId) : base(datareader)
    {
        BlockId = blockId;
        H0 = datareader.ReadInt32();
        H1 = datareader.ReadInt32();
        H2 = datareader.ReadInt32();

        Fields = new WriteSeqField[H0];
        for (var i = 0; i < H0; i++)
        {
            Fields[i] = MemoryMarshal.AsRef<WriteSeqField>(datareader.ReadBytes(4));
        }
    }
}

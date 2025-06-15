using System.IO;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MIDX" block.
/// </summary>
public class MeshIndexBuffer : Block
{
    public override BlockType Type => BlockType.MIDX;

    public override void Read(BinaryReader reader)
    {
        //
    }

    public override void WriteText(IndentedTextWriter writer)
    {
        writer.WriteLine("Not yet.");
    }
}

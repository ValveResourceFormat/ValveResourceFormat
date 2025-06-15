using System.IO;

namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MVTX" block.
/// </summary>
public class MeshVertexBuffer : Block
{
    public override BlockType Type => BlockType.MVTX;

    public override void Read(BinaryReader reader)
    {
        //
    }

    public override void WriteText(IndentedTextWriter writer)
    {
        writer.WriteLine("Not yet.");
    }
}

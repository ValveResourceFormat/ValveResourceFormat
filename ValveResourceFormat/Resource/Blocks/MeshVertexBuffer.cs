namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MVTX" block.
/// </summary>
public class MeshVertexBuffer : RawBinary
{
    public override BlockType Type => BlockType.MVTX;
}

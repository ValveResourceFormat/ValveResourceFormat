namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MIDX" block.
/// </summary>
public class MeshIndexBuffer : RawBinary
{
    public override BlockType Type => BlockType.MIDX;
}

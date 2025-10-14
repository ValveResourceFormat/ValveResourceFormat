namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MIDX" block.
/// </summary>
public class MeshIndexBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MIDX;
}

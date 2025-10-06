namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MVTX" block.
/// </summary>
public class MeshVertexBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MVTX;
}

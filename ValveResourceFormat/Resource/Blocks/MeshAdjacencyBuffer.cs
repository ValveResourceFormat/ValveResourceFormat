namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MADJ" block.
/// </summary>
public class MeshAdjacencyBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MADJ;
}

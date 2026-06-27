
namespace ValveResourceFormat.Blocks;

/// <summary>
/// "MSLT" block.
/// </summary>
public class MeshletBuffer : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => BlockType.MSLT;
}

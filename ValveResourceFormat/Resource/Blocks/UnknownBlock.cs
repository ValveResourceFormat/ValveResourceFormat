namespace ValveResourceFormat.Blocks;

/// <summary>
/// Opaque block used when a resource contains an unsupported block type.
/// </summary>
public sealed class UnknownBlock(BlockType type) : RawBinary
{
    /// <inheritdoc/>
    public override BlockType Type => type;
}

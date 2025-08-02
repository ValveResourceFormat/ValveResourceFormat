using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Resource data.
    /// </summary>
    public abstract class ResourceData : Block
    {
        public override BlockType Type => BlockType.DATA;
    }
}

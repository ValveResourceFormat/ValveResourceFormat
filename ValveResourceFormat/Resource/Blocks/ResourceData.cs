using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Resource data.
    /// </summary>
    public class ResourceData : Block
    {
        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            // TODO
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException("WriteText() in ResourceData");
        }
    }
}

using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    public class Map : Block
    {
        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            // Maps have no data
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            // Maps have no data
        }
    }
}

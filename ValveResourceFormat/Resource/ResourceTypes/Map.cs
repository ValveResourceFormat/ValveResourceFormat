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

        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            // Maps have no data
        }
    }
}

using System.IO;
using ValveResourceFormat.Blocks;

namespace ValveResourceFormat.ResourceTypes
{
    public class Map : ResourceData
    {
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

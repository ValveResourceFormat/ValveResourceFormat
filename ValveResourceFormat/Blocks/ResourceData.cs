using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Resource data.
    /// </summary>
    public class ResourceData : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.DATA;
        }

        public override void Read(BinaryReader reader)
        {
            
        }

        public override string ToString()
        {
            throw new NotImplementedException("ToString() in ResourceData");
        }
    }
}

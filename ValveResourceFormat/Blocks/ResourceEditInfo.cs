using System;
using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "REDI" block. ResourceEditInfoBlock_t
    /// </summary>
    public class ResourceEditInfo : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.REDI;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;
        }
    }
}

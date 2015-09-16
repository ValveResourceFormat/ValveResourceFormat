using System;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VBIB" block.
    /// </summary>
    public class VBIB : Block
    {
        public override BlockType GetChar()
        {
            return BlockType.VBIB;
        }

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = this.Offset;

            // TODO
        }

        public override string ToString()
        {
            return "";
        }
    }
}

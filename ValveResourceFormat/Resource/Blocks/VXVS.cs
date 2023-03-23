using System;
using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VXVS" block.
    /// </summary>
    public class VXVS : Block
    {
        public override BlockType Type => BlockType.VXVS;

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException();
        }
    }
}

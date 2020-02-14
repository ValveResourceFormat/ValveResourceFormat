using System;
using System.Collections.Generic;
using System.IO;
using System.Text;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "SNAP" block.
    /// </summary>
    public class SNAP : Block
    {
        public override BlockType Type => BlockType.SNAP;

        public override void Read(BinaryReader reader, Resource resource)
        {
            reader.BaseStream.Position = Offset;

            throw new NotImplementedException();
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("{0:X8}", Offset);
        }
    }
}

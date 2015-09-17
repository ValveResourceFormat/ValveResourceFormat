using System;
using System.CodeDom.Compiler;
using System.IO;

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

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("TODO: Don't know how to handle VBIB");
        }
    }
}

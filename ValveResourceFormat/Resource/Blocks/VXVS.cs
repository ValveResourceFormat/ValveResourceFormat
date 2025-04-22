using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// "VXVS" block.
    /// </summary>
    public class VXVS : Block
    {
        public override BlockType Type => BlockType.VXVS;

        public override void Read(BinaryReader reader)
        {
            reader.BaseStream.Position = Offset;
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            writer.WriteLine("Parsing world visiblity is not implemented. If you're up to the task, try to reverse engineer it!");
        }
    }
}

using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Unknown resource data.
    /// </summary>
    public class UnknownDataBlock : ResourceData
    {
        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            // TODO
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException("WriteText() in ResourceData");
        }
    }
}

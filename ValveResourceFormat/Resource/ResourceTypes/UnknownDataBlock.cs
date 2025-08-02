using System.IO;

namespace ValveResourceFormat.Blocks
{
    /// <summary>
    /// Unknown resource data.
    /// </summary>
    public class UnknownDataBlock(ResourceType ResourceType) : ResourceData
    {
        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            //
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException($"Unknown data block for resource type {ResourceType}");
        }
    }
}

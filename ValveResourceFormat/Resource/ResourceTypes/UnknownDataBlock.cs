using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Unknown resource data.
    /// </summary>
    public class UnknownDataBlock(ResourceType ResourceType) : Block
    {
        public override BlockType Type => BlockType.DATA;

        public override void Read(BinaryReader reader)
        {
            //
        }

        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException($"Unknown data block for resource type {ResourceType}");
        }

        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException($"Unknown data block for resource type {ResourceType}");
        }
    }
}

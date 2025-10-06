using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Unknown resource data.
    /// </summary>
    public class UnknownDataBlock(ResourceType ResourceType) : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            //
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException($"Unknown data block for resource type {ResourceType}");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            throw new NotImplementedException($"Unknown data block for resource type {ResourceType}");
        }
    }
}

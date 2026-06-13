using System.IO;

namespace ValveResourceFormat.ResourceTypes
{
    /// <summary>
    /// Map resource block.
    /// </summary>
    public class Map : Block
    {
        /// <inheritdoc/>
        public override BlockType Type => BlockType.DATA;

        /// <inheritdoc/>
        public override void Read(BinaryReader reader)
        {
            // Maps have no data
        }

        /// <inheritdoc/>
        public override void Serialize(Stream stream)
        {
            throw new NotImplementedException("Serializing this block is not yet supported. If you need this, send us a pull request!");
        }

        /// <inheritdoc/>
        public override void WriteText(IndentedTextWriter writer)
        {
            // Maps have no data
        }
    }
}

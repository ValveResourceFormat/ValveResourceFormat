using System.IO;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a block within the resource file.
    /// </summary>
    public abstract class Block
    {
        /// <summary>
        /// Gets the block type.
        /// </summary>
        public abstract BlockType Type { get; }

        /// <summary>
        /// Gets or sets the offset to the data.
        /// </summary>
        public uint Offset { get; set; }

        /// <summary>
        /// Gets or sets the data size.
        /// </summary>
        public uint Size { get; set; }

        /// <summary>
        /// Gets the resource this block belongs to.
        /// </summary>
        public Resource? Resource { get; set; }

        /// <summary>
        /// Reads the block data from a binary reader.
        /// </summary>
        /// <param name="reader">The binary reader to read from.</param>
        public abstract void Read(BinaryReader reader);

        /// <inheritdoc/>
        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);

            return writer.ToString();
        }

        /// <summary>
        /// Writes the correct text dump of the object to IndentedTextWriter.
        /// </summary>
        /// <param name="writer">IndentedTextWriter.</param>
        public abstract void WriteText(IndentedTextWriter writer);

        /// <summary>
        /// Writes the binary representation of the object to Stream.
        /// </summary>
        /// <param name="stream">Stream.</param>
        public abstract void Serialize(Stream stream);
    }
}

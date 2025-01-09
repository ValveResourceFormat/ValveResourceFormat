using System.IO;

namespace ValveResourceFormat
{
    /// <summary>
    /// Represents a block within the resource file.
    /// </summary>
    public abstract class Block
    {
        /// <summary>
        /// Gets the resource this block belongs to.
        /// </summary>
        public Resource Resource { get; set; }

        /// <summary>
        /// Has the block data been read from the resource stream?
        /// </summary>
        public bool IsLoaded { get; set; }

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

        public abstract void Read(BinaryReader reader);

        public void Read()
        {
            Read(Resource.Reader);
            IsLoaded = true;
        }

        public void EnsureLoaded()
        {
            if (!IsLoaded)
            {
                Read();
            }
        }

        /// <summary>
        /// Returns a string that represents the current object.
        /// </summary>
        /// <returns>A string that represents the current object.</returns>
        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);

            return writer.ToString();
        }

        /// <summary>
        /// Writers the correct object to IndentedTextWriter.
        /// </summary>
        /// <param name="writer">IndentedTextWriter.</param>
        public abstract void WriteText(IndentedTextWriter writer);
    }
}

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
        /// <remarks>
        /// Can technically be <c>null</c> if constructed outside of a <see cref="Resource"/>.
        /// </remarks>
        public required Resource Resource { get; set; }

        private volatile bool deferred;

        /// <summary>
        /// Gets whether the block data has been parsed. Only false for blocks a partial
        /// <see cref="Resource.Read(Stream, in ResourceReadOptions)"/> left unparsed; call
        /// <see cref="EnsureRead"/> to materialize such a block.
        /// </summary>
        public bool IsRead => !deferred;

        internal void MarkDeferred() => deferred = true;

        /// <summary>
        /// Parses the block data if a partial <see cref="Resource.Read(Stream, in ResourceReadOptions)"/>
        /// left it unparsed. Safe to call from multiple threads and a no-op once the block is parsed.
        /// The resource's input stream must still be open.
        /// </summary>
        public void EnsureRead()
        {
            if (!deferred)
            {
                return;
            }

            lock (Resource.BlockReadLock)
            {
                if (!deferred)
                {
                    return;
                }

                var reader = Resource.Reader
                    ?? throw new InvalidOperationException($"Cannot materialize deferred block {Type} because the resource's reader is no longer available.");

                Read(reader);
                deferred = false;
            }
        }

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

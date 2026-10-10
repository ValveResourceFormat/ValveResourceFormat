using System.IO;
using System.Runtime.ExceptionServices;

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
        private bool materializing;
        private ExceptionDispatchInfo? readFailure;

        /// <summary>
        /// Gets whether the block data has been parsed. Only false for blocks a
        /// <see cref="BlockParsing.Deferred"/> read left unparsed.
        /// </summary>
        internal bool IsRead => !deferred;

        internal void MarkDeferred() => deferred = true;

        /// <summary>
        /// Parses the block data if a <see cref="BlockParsing.Deferred"/> read left it unparsed.
        /// Safe to call from multiple threads and a no-op once the block is parsed.
        /// The resource's input stream must still be open. A block that fails to parse
        /// throws the same exception on every later call.
        /// </summary>
        internal void EnsureRead()
        {
            if (!deferred)
            {
                return;
            }

            lock (Resource.ReaderLock)
            {
                if (!deferred || materializing)
                {
                    return;
                }

                readFailure?.Throw();

                var reader = Resource.Reader
                    ?? throw new InvalidOperationException($"Cannot materialize deferred block {Type} because the resource's reader is no longer available.");

                var position = reader.BaseStream.Position;
                materializing = true;

                try
                {
                    Read(reader);
                    deferred = false;
                }
                catch (Exception e)
                {
                    readFailure = ExceptionDispatchInfo.Capture(e);
                    throw;
                }
                finally
                {
                    materializing = false;
                    reader.BaseStream.Position = position;
                }
            }
        }

        /// <summary>
        /// Reads the block data from a binary reader.
        /// </summary>
        /// <param name="reader">The binary reader to read from.</param>
        public abstract void Read(BinaryReader reader);

        internal byte[] ReadRawData()
        {
            var data = new byte[Size];

            lock (Resource.ReaderLock)
            {
                var reader = Resource.Reader ?? throw new InvalidOperationException($"Cannot read block {Type} because the resource's reader is not available.");

                reader.BaseStream.Position = Offset;
                reader.BaseStream.ReadExactly(data);
            }

            return data;
        }

        /// <inheritdoc/>
        public override string ToString()
        {
            using var writer = new IndentedTextWriter();
            WriteText(writer);

            return writer.ToString();
        }

        /// <summary>
        /// Writes the correct text dump of the object to <see cref="IndentedTextWriter"/>.
        /// </summary>
        /// <param name="writer"><see cref="IndentedTextWriter"/>.</param>
        public abstract void WriteText(IndentedTextWriter writer);

        /// <summary>
        /// Writes the binary representation of the object to Stream.
        /// </summary>
        /// <param name="stream">Stream.</param>
        public abstract void Serialize(Stream stream);
    }
}

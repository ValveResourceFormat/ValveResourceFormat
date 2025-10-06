#nullable disable

namespace ValveResourceFormat.ClosedCaptions
{
    /// <summary>
    /// Represents a single closed caption entry.
    /// </summary>
    public class ClosedCaption
    {
        /// <summary>
        /// Gets or sets the hash of the caption key.
        /// </summary>
        public uint Hash { get; set; }

        /// <summary>
        /// Gets or sets the hash of the caption text (version 2+).
        /// </summary>
        public uint HashText { get; set; }

        /// <summary>
        /// Gets or sets the block number where the caption text is stored.
        /// </summary>
        public int Blocknum { get; set; }

        /// <summary>
        /// Gets or sets the offset within the block.
        /// </summary>
        public ushort Offset { get; set; }

        /// <summary>
        /// Gets or sets the length of the caption text.
        /// </summary>
        public ushort Length { get; set; }

        /// <summary>
        /// Gets or sets the caption text.
        /// </summary>
        public string Text { get; set; }
    }
}

using System.Linq;

namespace ValveResourceFormat
{
    /// <summary>
    /// Options controlling how <see cref="Resource.Read(System.IO.Stream, in ResourceReadOptions)"/> parses a resource.
    /// The default value reproduces the standard behavior: every block is parsed, the file size is verified,
    /// and the input stream is closed when the resource is disposed.
    /// </summary>
    public readonly record struct ResourceReadOptions
    {
        /// <summary>
        /// Block types to parse, or <c>null</c> to parse all blocks. <see cref="BlockType.NTRO"/>,
        /// <see cref="BlockType.REDI"/> and <see cref="BlockType.RED2"/> are always parsed regardless of
        /// this selection because other blocks depend on them. Blocks left out are still indexed with
        /// their offset and size, and <see cref="Block.EnsureRead"/> materializes them on demand while
        /// the input stream remains open.
        /// </summary>
        public IReadOnlyCollection<BlockType>? IncludeBlocks { get; init; }

        /// <summary>
        /// Block types to skip, applied after <see cref="IncludeBlocks"/>. Listing a type in both sets
        /// throws <see cref="ArgumentException"/> when reading.
        /// </summary>
        public IReadOnlyCollection<BlockType>? ExcludeBlocks { get; init; }

        /// <summary>
        /// Skip verifying that the stream length matches the size specified in the file. Verification is
        /// also skipped automatically whenever the block selection leaves any block unparsed.
        /// </summary>
        public bool SkipFileSizeVerification { get; init; }

        /// <summary>
        /// Leave the input stream open after the resource is disposed.
        /// </summary>
        public bool LeaveOpen { get; init; }

        internal void Validate()
        {
            if (IncludeBlocks == null || ExcludeBlocks == null)
            {
                return;
            }

            foreach (var type in ExcludeBlocks)
            {
                if (IncludeBlocks.Contains(type))
                {
                    throw new ArgumentException($"Block type {type} is listed in both {nameof(IncludeBlocks)} and {nameof(ExcludeBlocks)}.");
                }
            }
        }

        internal bool ShouldParse(BlockType type)
        {
            if (type is BlockType.NTRO or BlockType.REDI or BlockType.RED2)
            {
                return true;
            }

            if (IncludeBlocks != null && !IncludeBlocks.Contains(type))
            {
                return false;
            }

            if (ExcludeBlocks != null && ExcludeBlocks.Contains(type))
            {
                return false;
            }

            return true;
        }
    }
}

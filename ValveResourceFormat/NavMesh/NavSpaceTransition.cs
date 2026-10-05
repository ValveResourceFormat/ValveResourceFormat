namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// A connection between two blocks of a <see cref="NavSpaceFile"/>. Transitions are undirected, and each pair of
    /// blocks is stored once.
    /// </summary>
    public class NavSpaceTransition
    {
        /// <summary>
        /// Gets the <see cref="NavSpaceBlock.Id"/> of the first block.
        /// </summary>
        public uint FromBlockId { get; init; }

        /// <summary>
        /// Gets the <see cref="NavSpaceBlock.Id"/> of the second block.
        /// </summary>
        public uint ToBlockId { get; init; }

        /// <summary>
        /// Gets the bounding box of the transition.
        /// </summary>
        public AABB Bounds { get; init; }

        /// <summary>
        /// Gets the vertices of the region where the hulls of both blocks overlap, in no particular order.
        /// Blocks that only touch use the overlap of their hulls grown by 2 units.
        /// </summary>
        public Vector3[] Vertices { get; init; } = [];
    }
}

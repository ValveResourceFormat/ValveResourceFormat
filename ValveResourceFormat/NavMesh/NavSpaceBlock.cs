namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// A convex volume of a <see cref="NavSpaceFile"/> in which the centre of a flying agent can move freely.
    /// Blocks never extend past their grid cell, and neighbouring blocks may overlap.
    /// </summary>
    public class NavSpaceBlock
    {
        /// <summary>
        /// Gets the block id, numbering all blocks of the file from 1 in file order.
        /// </summary>
        public uint Id { get; init; }

        /// <summary>
        /// Gets the index of the grid cell this block belongs to, see <see cref="NavSpaceFile.Cells"/>.
        /// </summary>
        public int CellIndex { get; init; }

        /// <summary>
        /// Gets whether the block touches a face of its grid cell. Only these blocks can have transitions to blocks
        /// in neighbouring cells.
        /// </summary>
        public bool TouchesCellBoundary { get; init; }

        /// <summary>
        /// Gets the mean of the block's hull vertices.
        /// </summary>
        public Vector3 Position { get; init; }

        /// <summary>
        /// Gets the bounding box of the block.
        /// </summary>
        public AABB Bounds { get; init; }

        /// <summary>
        /// Gets the vertices of the convex hull of the block, in no particular order.
        /// </summary>
        public Vector3[] Vertices { get; init; } = [];
    }
}

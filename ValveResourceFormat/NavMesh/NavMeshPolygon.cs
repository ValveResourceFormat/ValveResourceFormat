namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Represents a polygon in a navigation mesh.
    /// </summary>
    public readonly struct NavMeshPolygon
    {
        /// <summary>
        /// Gets the corner vertices of this polygon.
        /// </summary>
        public Vector3[] Corners { get; init; }

        /// <summary>
        /// Gets the gravity direction at each corner, parallel to <see cref="Corners"/>,
        /// or <see langword="null"/> when the file does not store gravity directions.
        /// Only stored in version 32 and newer.
        /// </summary>
        public Vector3[]? CornerGravity { get; init; }

        /// <summary>
        /// Gets the id of the movable nav mesh this polygon belongs to,
        /// or <see cref="NavMeshFile.NoMovableMesh"/> when it belongs to the static world.
        /// Only stored in version 35 and newer.
        /// </summary>
        public uint MovableMeshId { get; init; }
    }
}

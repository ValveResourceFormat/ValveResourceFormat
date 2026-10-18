namespace ValveResourceFormat.Particles
{
    /// <summary>How a function picks the snapshot point each particle reads.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SnapshotIndexType_t">SnapshotIndexType_t</seealso>
    internal enum SnapshotIndexType
    {
        /// <summary>A walk through the points, or a random point.</summary>
        SNAPSHOT_INDEX_INCREMENT = 0,

        /// <summary>The point a read index input names.</summary>
        SNAPSHOT_INDEX_DIRECT = 1,
    }
}

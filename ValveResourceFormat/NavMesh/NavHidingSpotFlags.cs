namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Classification flags of a <see cref="NavMeshHidingSpot"/>, computed when the navigation mesh is analyzed.
    /// </summary>
    [Flags]
    public enum NavHidingSpotFlags : byte
    {
        /// <summary>
        /// No flags are set.
        /// </summary>
        None = 0,

        /// <summary>
        /// The spot is in cover.
        /// </summary>
        InCover = 0x01,

        /// <summary>
        /// The spot has a good view for sniping.
        /// </summary>
        GoodSniperSpot = 0x02,

        /// <summary>
        /// The spot has an ideal view for sniping.
        /// </summary>
        IdealSniperSpot = 0x04,

        /// <summary>
        /// The spot is not in cover.
        /// </summary>
        Exposed = 0x08,
    }
}

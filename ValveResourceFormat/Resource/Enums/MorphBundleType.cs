namespace ValveResourceFormat
{
    /// <summary>
    /// Identifies the type of data stored in a morph bundle.
    /// Corresponds to <c>MorphBundleType_t</c>.
    /// </summary>
    public enum MorphBundleType
    {
        /// <summary>No morph bundle data.</summary>
        None = 0,

        /// <summary>Bundle stores per-vertex position delta and speed values.</summary>
        PositionSpeed = 1,

        /// <summary>Bundle stores per-vertex normal delta and wrinkle map values.</summary>
        NormalWrinkle = 2,
    }
}

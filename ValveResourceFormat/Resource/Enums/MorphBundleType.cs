namespace ValveResourceFormat
{
    /// <summary>
    /// Identifies the type of data stored in a morph bundle.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/modellib/MorphBundleType_t">MorphBundleType_t</seealso>
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

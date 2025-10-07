namespace ValveResourceFormat.ResourceTypes.ModelAnimation
{
    /// <summary>
    /// Represents the transform of a bone in a single animation frame.
    /// </summary>
    public struct FrameBone
    {
        /// <summary>
        /// Gets or sets the position of the bone.
        /// </summary>
        public Vector3 Position { get; set; }

        /// <summary>
        /// Gets or sets the rotation of the bone.
        /// </summary>
        public Quaternion Angle { get; set; }

        /// <summary>
        /// Gets or sets the scale of the bone.
        /// </summary>
        public float Scale { get; set; }
    }
}

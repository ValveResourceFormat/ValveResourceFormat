namespace ValveResourceFormat
{
    /// <summary>
    /// Flags describing how root motion is applied from an animation segment.
    /// Corresponds to the <c>motionflags</c> field in <c>CAnimMovement</c>.
    /// </summary>
    [Flags]
    public enum ModelAnimationMotionFlags
    {
        /// <summary>Root motion is applied along the X axis (translate X).</summary>
        TX = 64,

        /// <summary>Root motion is applied along the Y axis (translate Y).</summary>
        TY = 128,

        /// <summary>Root motion is applied along the Z axis (translate Z).</summary>
        TZ = 256,

        /// <summary>Root motion is applied as a rotation around the Z axis (rotate Z / yaw).</summary>
        RZ = 2048,

        /// <summary>Root motion uses linear (constant-velocity) interpolation instead of curve-based.</summary>
        Linear = 4096,
    }
}

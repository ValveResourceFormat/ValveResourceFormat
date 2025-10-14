namespace ValveResourceFormat
{
    /// <summary>
    /// Model animation motion flags.
    /// </summary>
    [Flags]
    public enum ModelAnimationMotionFlags
    {
#pragma warning disable CS1591
        TX = 64,
        TY = 128,
        TZ = 256,
        RZ = 2048,
        Linear = 4096,
#pragma warning restore CS1591
    }
}

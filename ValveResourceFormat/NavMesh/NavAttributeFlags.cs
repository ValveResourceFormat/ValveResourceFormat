namespace ValveResourceFormat.NavMesh
{
    /// <summary>
    /// Attribute flags stored on navigation mesh areas.
    /// </summary>
    /// <remarks>
    /// These values apply to nav version 35 and newer, older versions store a different set of flags which is not mapped here.
    /// Bits 19 and above are game specific attributes.
    /// </remarks>
    [Flags]
    public enum NavAttributeFlags : long
    {
#pragma warning disable CS1591
        None = 0,
        Jump = 0x2,
        NoJump = 0x8,
        Stop = 0x10,
        Run = 0x20,
        Walk = 0x40,
        Avoid = 0x80,
        Transient = 0x100,
        DontHide = 0x200,
        Stand = 0x400,
        NoHostages = 0x800,
        Stairs = 0x1000,
        NoMerge = 0x2000,
        ObstacleTop = 0x4000,
        NonZUp = 0x8000,
        CrouchHeight = 0x10000,
        NonZUpTransition = 0x20000,
        CrawlHeight = 0x40000,
#pragma warning restore CS1591
    }
}

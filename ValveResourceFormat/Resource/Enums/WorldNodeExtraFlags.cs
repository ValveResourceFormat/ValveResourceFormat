namespace ValveResourceFormat
{
    /// <summary>
    /// Settings the map compiler encodes in a world node model name that have no <see cref="ObjectTypeFlags"/> bit.
    /// </summary>
    [Flags]
    public enum WorldNodeExtraFlags
    {
        /// <summary>No flags set.</summary>
        None = 0,

        /// <summary>The Hammer mesh has Keep Vertices set, <c>_kv</c>.</summary>
        KeepVertices = 0x1,

        /// <summary>The material skips the depth prepass, <c>_nz</c> or <c>_nzp</c> from older compilers.</summary>
        NoZPrepass = 0x2,

        /// <summary>The material only renders in the forward rendering layer, <c>_fw</c>.</summary>
        ForwardLayerOnly = 0x4,
    }
}

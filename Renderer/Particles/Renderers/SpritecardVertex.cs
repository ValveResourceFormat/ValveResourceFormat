using System.Runtime.InteropServices;

namespace ValveResourceFormat.Renderer.Particles.Renderers
{
    /// <summary>
    /// One corner of a spritecard strip, for renderers that supply vertices rather than generating them.
    /// The shader looks the sheet frames up itself and applies the size clamp and the fades, so a corner
    /// carries the particle values those read rather than placed coordinates.
    /// </summary>
    [StructLayout(LayoutKind.Sequential)]
    internal struct SpritecardVertex
    {
        /// <summary>The corner, placed at <see cref="RotationRadius"/>'s radius along <see cref="WidthAxis"/> before the size clamp.</summary>
        [VertexAttribute(VertexSlot.Position)] public Vector3 Position;

        /// <summary>The colour, its alpha before any fade.</summary>
        [VertexAttribute(VertexSlot.Color)] public Vector4 Color;

        /// <summary>
        /// The random value, shader extra data 1, the age the sheet and the per particle modes play by, and
        /// shader extra data 2.
        /// </summary>
        [VertexAttribute(VertexSlot.TexCoord)] public Vector4 SheetInputs;

        /// <summary>The card corner after flipping in xy, and the V before it is mapped to the texture in z.</summary>
        [VertexAttribute(VertexSlot.TexCoord1)] public Vector3 CornerV;

        /// <summary>Roll, yaw and pitch, and the radius before the size clamp.</summary>
        [VertexAttribute(VertexSlot.TexCoord3)] public Vector4 RotationRadius;

        /// <summary>The unit axis from the strip's centre line out to this corner.</summary>
        [VertexAttribute("vAxisU")] public Vector3 WidthAxis;

        /// <summary>The axis whose alignment with the view fades the strip out by view angle.</summary>
        [VertexAttribute(VertexSlot.Normal)] public Vector3 FacingAxis;

        /// <summary>The alpha and the radius of the particle the segment starts at, which the per particle modes read.</summary>
        [VertexAttribute("vSegmentAlphaRadius")] public Vector2 SegmentAlphaRadius;

        /// <summary>
        /// Every attribute name <c>particle_spritecard.vert.slang</c> declares, across all of its combos.
        /// Keep in step with that file: both sides allocate locations over the whole set.
        /// </summary>
        public static readonly string[] DeclaredAttributeNames =
        [
            VertexAttributeLocations.GetName(VertexSlot.Position),
            VertexAttributeLocations.GetName(VertexSlot.Color),
            VertexAttributeLocations.GetName(VertexSlot.TexCoord),
            VertexAttributeLocations.GetName(VertexSlot.TexCoord1),
            VertexAttributeLocations.GetName(VertexSlot.TexCoord3),
            VertexAttributeLocations.GetName(VertexSlot.Normal),
            "vSegmentAlphaRadius",
            "vAxisU",
            "vAxisV",
            "vTrailUvCrop",
        ];

        /// <summary>The layout of this vertex, for creating vertex array objects.</summary>
        public static readonly VertexInputLayout InputLayout = VertexInputLayout.FromStruct<SpritecardVertex>(DeclaredAttributeNames);
    }
}

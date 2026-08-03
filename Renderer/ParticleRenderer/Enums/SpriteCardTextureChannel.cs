namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// How a texture layer's sampled channels are reduced against the accumulated result of the
    /// layers before it.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SpriteCardTextureChannel_t">SpriteCardTextureChannel_t</seealso>
    public enum SpriteCardTextureChannel
    {
        /// <summary>Colour from B.rgb, alpha from A.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB = 0,
        /// <summary>Colour from B.rgb, alpha from B.a (standard).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA = 1,
        /// <summary>Colour from A, alpha from B.a.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_A = 2,
        /// <summary>Colour from B.rgb, alpha from A.a * B.a.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A = 3,
        /// <summary>B.rgb blended over A.rgb by B.a, keeping A's alpha.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK = 4,
        /// <summary>B.rgb blended over A.rgb by B's luminance, keeping A's alpha.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK = 5,
        /// <summary>Colour from B.rgb, alpha from B's luminance.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA = 6,
        /// <summary>Colour from A, alpha from B's luminance.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA = 7,
        /// <summary>Colour from B.rgb, alpha from A.a scaled by B's luminance.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA = 8,
        /// <summary>Colour from B's red channel, alpha from A.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_R = 9,
        /// <summary>Colour from B's green channel, alpha from A.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_G = 10,
        /// <summary>Colour from B's blue channel, alpha from A.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_B = 11,
        /// <summary>B's red channel broadcast into colour and alpha alike.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA = 12,
        /// <summary>B's green channel broadcast into colour and alpha alike.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA = 13,
        /// <summary>B's blue channel broadcast into colour and alpha alike.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA = 14,
    }
}

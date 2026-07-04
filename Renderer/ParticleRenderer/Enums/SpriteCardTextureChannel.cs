namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// How a sprite renderer's sampled texture channels are mapped into the output colour and alpha.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SpriteCardTextureChannel_t">SpriteCardTextureChannel_t</seealso>
    public enum SpriteCardTextureChannel
    {
        /// <summary>Colour from RGB; alpha ignored (opaque).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB = 0,
        /// <summary>Colour from RGB, alpha from A (standard).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA = 1,
        /// <summary>Alpha-only texture: colour comes from the particle, alpha from A.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_A = 2,
        /// <summary>Colour from RGB, alpha from A of a second texture (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A = 3,
        /// <summary>Colour from RGB masked by a second texture's alpha (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_ALPHAMASK = 4,
        /// <summary>Colour from RGB masked by a second texture's RGB (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_RGBMASK = 5,
        /// <summary>RGBA blended with a second texture's RGB+alpha (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGBA_RGBALPHA = 6,
        /// <summary>Alpha blended with a second texture's RGB+alpha (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_A_RGBALPHA = 7,
        /// <summary>RGB+alpha blended with a second texture's RGB+alpha (dual-texture).</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RGB_A_RGBALPHA = 8,
        /// <summary>Colour from the red channel only.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_R = 9,
        /// <summary>Colour from the green channel only.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_G = 10,
        /// <summary>Colour from the blue channel only.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_B = 11,
        /// <summary>Alpha from the red channel; colour comes from the particle.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_RALPHA = 12,
        /// <summary>Alpha from the green channel; colour comes from the particle.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_GALPHA = 13,
        /// <summary>Alpha from the blue channel; colour comes from the particle.</summary>
        SPRITECARD_TEXTURE_CHANNEL_MIX_BALPHA = 14,
    }
}

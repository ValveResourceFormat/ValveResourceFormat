namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// What role a texture layer plays. DIFFUSE through UVDISTORTION_ZOOM are colour layers, differing in
    /// which effect is applied to the sample; NORMALMAP and ANIMMOTIONVEC are not colour at all and must
    /// stay out of the colour chain.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SpriteCardTextureType_t">SpriteCardTextureType_t</seealso>
    public enum SpriteCardTextureType
    {
        /// <summary>A plain colour layer.</summary>
        SPRITECARD_TEXTURE_DIFFUSE = 0,
        /// <summary>Colour, blurred by a time-driven zoom about the card centre.</summary>
        SPRITECARD_TEXTURE_ZOOM = 1,
        /// <summary>A gradient ramp, looked up by the luminance of what is beneath it.</summary>
        SPRITECARD_TEXTURE_1D_COLOR_LOOKUP = 2,
        /// <summary>Its red and green offset where the previous layer is sampled from.</summary>
        SPRITECARD_TEXTURE_UVDISTORTION = 3,
        /// <summary>Zoom blur, then distortion driven by the blurred result.</summary>
        SPRITECARD_TEXTURE_UVDISTORTION_ZOOM = 4,
        /// <summary>A normal map for lighting, not a colour contribution.</summary>
        SPRITECARD_TEXTURE_NORMALMAP = 5,
        /// <summary>Motion vectors for sheet frame interpolation, not a colour contribution.</summary>
        SPRITECARD_TEXTURE_ANIMMOTIONVEC = 6,
    }
}

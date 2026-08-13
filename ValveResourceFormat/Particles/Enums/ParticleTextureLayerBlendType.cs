namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a texture layer, once reduced by its <see cref="SpriteCardTextureChannel"/>, is combined with
    /// the accumulated result of the layers before it.
    /// </summary>
    /// <remarks>
    /// The accumulator starts at white, so with a single layer MULTIPLY is the identity and the other
    /// modes operate against 1 rather than against another texture.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleTextureLayerBlendType_t">ParticleTextureLayerBlendType_t</seealso>
    public enum ParticleTextureLayerBlendType
    {
        /// <summary>accumulator * layer.</summary>
        SPRITECARD_TEXTURE_BLEND_MULTIPLY = 0,
        /// <summary>accumulator * layer * 2, so that 0.5 in the layer is neutral.</summary>
        SPRITECARD_TEXTURE_BLEND_MOD2X = 1,
        /// <summary>The layer replaces the accumulator outright.</summary>
        SPRITECARD_TEXTURE_BLEND_REPLACE = 2,
        /// <summary>accumulator + layer.</summary>
        SPRITECARD_TEXTURE_BLEND_ADD = 3,
        /// <summary>accumulator - layer.</summary>
        SPRITECARD_TEXTURE_BLEND_SUBTRACT = 4,
        /// <summary>The mean of the accumulator and the layer.</summary>
        SPRITECARD_TEXTURE_BLEND_AVERAGE = 5,
        /// <summary>The accumulator scaled by the layer's luminance, keeping the layer's alpha.</summary>
        SPRITECARD_TEXTURE_BLEND_LUMINANCE = 6,
    }
}

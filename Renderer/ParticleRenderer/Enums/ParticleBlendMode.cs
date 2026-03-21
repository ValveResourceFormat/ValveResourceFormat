namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle render output blending modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOutputBlendMode_t">ParticleOutputBlendMode_t</seealso>
    public enum ParticleBlendMode
    {
        /// <summary>Standard alpha blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_ALPHA = 0,
        /// <summary>Additive blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_ADD = 1,
        /// <summary>Blend then add blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_BLEND_ADD = 2,
        /// <summary>Half blend then add blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_HALF_BLEND_ADD = 3,
        /// <summary>Negative half blend then add blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_NEG_HALF_BLEND_ADD = 4,
        /// <summary>Modulate 2x blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_MOD2X = 5,
        /// <summary>Lighten blending.</summary>
        PARTICLE_OUTPUT_BLEND_MODE_LIGHTEN = 6,
    }
}

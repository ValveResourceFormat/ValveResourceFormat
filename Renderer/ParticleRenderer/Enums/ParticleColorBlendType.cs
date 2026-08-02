namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle color blend types.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleColorBlendType_t">ParticleColorBlendType_t</seealso>
    public enum ParticleColorBlendType
    {
        /// <summary>Multiply.</summary>
        PARTICLE_COLOR_BLEND_MULTIPLY = 0,
        /// <summary>Multiply x2.</summary>
        PARTICLE_COLOR_BLEND_MULTIPLY2X = 1,
        /// <summary>Divide.</summary>
        PARTICLE_COLOR_BLEND_DIVIDE = 2,
        /// <summary>Add.</summary>
        PARTICLE_COLOR_BLEND_ADD = 3,
        /// <summary>Subtract.</summary>
        PARTICLE_COLOR_BLEND_SUBTRACT = 4,
        /// <summary>Mod2X.</summary>
        PARTICLE_COLOR_BLEND_MOD2X = 5,
        /// <summary>Screen.</summary>
        PARTICLE_COLOR_BLEND_SCREEN = 6,
        /// <summary>Lighten: the per-channel maximum.</summary>
        PARTICLE_COLOR_BLEND_MAX = 7,
        /// <summary>Darken: the per-channel minimum.</summary>
        PARTICLE_COLOR_BLEND_MIN = 8,
        /// <summary>Replace.</summary>
        PARTICLE_COLOR_BLEND_REPLACE = 9,
        /// <summary>Average.</summary>
        PARTICLE_COLOR_BLEND_AVERAGE = 10,
        /// <summary>Negate.</summary>
        PARTICLE_COLOR_BLEND_NEGATE = 11,
        /// <summary>Luminance.</summary>
        PARTICLE_COLOR_BLEND_LUMINANCE = 12,
    }
}

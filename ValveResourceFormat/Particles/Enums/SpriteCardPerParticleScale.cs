namespace ValveResourceFormat.Particles
{
    /// <summary>The per particle value a spritecard input reads.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/SpriteCardPerParticleScale_t">SpriteCardPerParticleScale_t</seealso>
    public enum SpriteCardPerParticleScale
    {
        /// <summary>No value; the input reads 1.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_NONE = 0,
        /// <summary>The particle's age plus 1.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_AGE = 1,
        /// <summary>The particle's animation frame.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_ANIMATION_FRAME = 2,
        /// <summary>The particle's first shader extra data value.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA1 = 3,
        /// <summary>The particle's second shader extra data value.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_SHADER_EXTRA_DATA2 = 4,
        /// <summary>The particle's alpha.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_PARTICLE_ALPHA = 5,
        /// <summary>The particle's radius.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_SHADER_RADIUS = 6,
        /// <summary>The particle's roll.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_ROLL = 7,
        /// <summary>The particle's yaw.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_YAW = 8,
        /// <summary>The particle's pitch.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_PITCH = 9,
        /// <summary>A random value per particle in 0 to 1.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_RANDOM = 10,
        /// <summary>A random value per particle in -1 to 1.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_NEG_RANDOM = 11,
        /// <summary>The particle's age scaled by a random value, plus 1.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_RANDOM_TIME = 12,
        /// <summary>The particle's age scaled by a random value in -1 to 1, offset away from 0.</summary>
        SPRITECARD_TEXTURE_PP_SCALE_NEG_RANDOM_TIME = 13,
    }
}

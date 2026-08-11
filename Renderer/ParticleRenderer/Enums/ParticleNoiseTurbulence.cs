namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Turbulence post-passes for noise-typed particle float inputs.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/PFNoiseTurbulence_t">PFNoiseTurbulence_t</seealso>
    public enum ParticleNoiseTurbulence
    {
        /// <summary>No turbulence pass.</summary>
        PF_NOISE_TURB_NONE = 0,
        /// <summary>Mixes toward an inverted-ridge sample at a fixed offset.</summary>
        PF_NOISE_TURB_HIGHLIGHT = 1,
        /// <summary>Mixes toward a sample displaced by the base noise value.</summary>
        PF_NOISE_TURB_FEEDBACK = 2,
        /// <summary>Mixes toward a sample displaced by a triangle wave of the base value.</summary>
        PF_NOISE_TURB_LOOPY = 3,
        /// <summary>Mixes toward a doubled, clamped sample displaced by the base value's complement.</summary>
        PF_NOISE_TURB_CONTRAST = 4,
        /// <summary>Mixes toward the product of the base value and a displaced sample.</summary>
        PF_NOISE_TURB_ALTERNATE = 5,
    }

    /// <summary>
    /// Output-shaping modifiers for noise-typed particle float inputs.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/PFNoiseModifier_t">PFNoiseModifier_t</seealso>
    public enum ParticleNoiseModifier
    {
        /// <summary>Maps the signed noise into 0-1.</summary>
        PF_NOISE_MODIFIER_NONE = 0,
        /// <summary>Banded sine-squared response.</summary>
        PF_NOISE_MODIFIER_LINES = 1,
        /// <summary>Amplified absolute value.</summary>
        PF_NOISE_MODIFIER_CLUMPS = 2,
        /// <summary>Concentric ring response.</summary>
        PF_NOISE_MODIFIER_RINGS = 3,
    }

    /// <summary>
    /// Primitive selection for noise-typed particle float inputs.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/PFNoiseType_t">PFNoiseType_t</seealso>
    public enum ParticleNoiseType
    {
        /// <summary>Value-noise lattice, the default.</summary>
        PF_NOISE_TYPE_PERLIN = 0,
        /// <summary>Gradient noise with a quintic fade.</summary>
        PF_NOISE_TYPE_SIMPLEX = 1,
        /// <summary>Squared distance to the nearest jittered cell feature point.</summary>
        PF_NOISE_TYPE_WORLEY = 2,
        /// <summary>Curl of a vector-valued lattice; scalar inputs read its first component.</summary>
        PF_NOISE_TYPE_CURL = 3,
    }

    /// <summary>
    /// Vector noise selection for direction noise forces.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleDirectionNoiseType_t">ParticleDirectionNoiseType_t</seealso>
    public enum ParticleDirectionNoiseType
    {
        /// <summary>Vector-valued lattice noise.</summary>
        PARTICLE_DIR_NOISE_PERLIN = 0,
        /// <summary>Curl combination of three decorrelated lattice samples.</summary>
        PARTICLE_DIR_NOISE_CURL = 1,
        /// <summary>Offset to the nearest jittered worley feature point.</summary>
        PARTICLE_DIR_NOISE_WORLEY_BASIC = 2,
    }
}

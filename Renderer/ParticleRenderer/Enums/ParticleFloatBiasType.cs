namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Bias curve types for modulating floating-point particle parameters.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleFloatBiasType_t">ParticleFloatBiasType_t</seealso>
    public enum ParticleFloatBiasType
    {
        /// <summary>Invalid bias type.</summary>
        PF_BIAS_TYPE_INVALID = -1,
        /// <summary>Standard S-curve bias.</summary>
        PF_BIAS_TYPE_STANDARD = 0,
        /// <summary>Gain bias curve.</summary>
        PF_BIAS_TYPE_GAIN = 1,
        /// <summary>Exponential bias curve.</summary>
        PF_BIAS_TYPE_EXPONENTIAL = 2,
    }
}

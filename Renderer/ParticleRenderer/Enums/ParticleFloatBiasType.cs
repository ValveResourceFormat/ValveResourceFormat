namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Bias curve types for modulating floating-point particle parameters.
    /// </summary>
    public enum ParticleFloatBiasType // ParticleFloatBiasType_t
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        PF_BIAS_TYPE_INVALID = -1,
        PF_BIAS_TYPE_STANDARD = 0,
        PF_BIAS_TYPE_GAIN = 1,
        PF_BIAS_TYPE_EXPONENTIAL = 2,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}

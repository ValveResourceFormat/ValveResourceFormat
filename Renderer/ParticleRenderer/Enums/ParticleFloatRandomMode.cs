namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle random value generation modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particleslib/ParticleFloatRandomMode_t">ParticleFloatRandomMode_t</seealso>
    public enum ParticleFloatRandomMode
    {
        /// <summary>
        /// Invalid random mode.
        /// </summary>
        PF_RANDOM_MODE_INVALID = -1,

        /// <summary>
        /// Random per-particle but doesn't change per frame.
        /// </summary>
        PF_RANDOM_MODE_CONSTANT = 0,

        /// <summary>
        /// Random per-particle, per-frame.
        /// </summary>
        PF_RANDOM_MODE_VARYING = 1,
    }
}

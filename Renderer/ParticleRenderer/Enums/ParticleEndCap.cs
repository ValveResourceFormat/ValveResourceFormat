namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle system endcap behavior when reaching lifetime limits.
    /// </summary>
    public enum ParticleEndCapMode
    {
        /// <summary>The operator always runs, regardless of endcap state.</summary>
        PARTICLE_ENDCAP_ALWAYS_ON = -1,
        /// <summary>The operator runs only when the endcap is not active.</summary>
        PARTICLE_ENDCAP_ENDCAP_OFF = 0,
        /// <summary>The operator runs only during the endcap phase.</summary>
        PARTICLE_ENDCAP_ENDCAP_ON = 1,
    }
}

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle system endcap behavior when reaching lifetime limits.
    /// </summary>
    public enum ParticleEndCapMode
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        PARTICLE_ENDCAP_ALWAYS_ON = -1,
        PARTICLE_ENDCAP_ENDCAP_OFF = 0,
        PARTICLE_ENDCAP_ENDCAP_ON = 1,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}

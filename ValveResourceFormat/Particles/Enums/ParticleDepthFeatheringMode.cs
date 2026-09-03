namespace ValveResourceFormat.Particles
{
    /// <summary>Particle depth feathering option.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleDepthFeatheringMode_t">ParticleDepthFeatheringMode_t</seealso>
    public enum ParticleDepthFeatheringMode
    {
        /// <summary>No feathering; cards intersect geometry with a hard edge.</summary>
        PARTICLE_DEPTH_FEATHERING_OFF = 0,
        /// <summary>Feathering only in high settings.</summary>
        PARTICLE_DEPTH_FEATHERING_ON_OPTIONAL = 1,
        /// <summary>Feathering always on.</summary>
        PARTICLE_DEPTH_FEATHERING_ON_REQUIRED = 2,
    }
}

namespace ValveResourceFormat.Particles
{
    /// <summary>Where a particle renderer samples the scene lighting.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightingQuality_t">ParticleLightingQuality_t</seealso>
    public enum ParticleLightingQuality
    {
        /// <summary>At every pixel.</summary>
        PARTICLE_LIGHTING_PER_PIXEL = -1,
        /// <summary>Once per particle.</summary>
        PARTICLE_LIGHTING_PER_PARTICLE = 0,
        /// <summary>At every vertex.</summary>
        PARTICLE_LIGHTING_PER_VERTEX = 1,
        /// <summary>From a position given by the lighting override input.</summary>
        PARTICLE_LIGHTING_OVERRIDE_POSITION = 2,
        /// <summary>With a color given by the lighting override input.</summary>
        PARTICLE_LIGHTING_OVERRIDE_COLOR = 3,
        /// <summary>Adding a color given by the lighting override input.</summary>
        PARTICLE_LIGHTING_ADD_EXTRA_LIGHT = 4,
    }
}

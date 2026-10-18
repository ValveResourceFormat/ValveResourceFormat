namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// What kind of light a standard particle light casts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightTypeChoiceList_t">ParticleLightTypeChoiceList_t</seealso>
    public enum ParticleLightTypeChoiceList
    {
        /// <summary>A point light.</summary>
        PARTICLE_LIGHT_TYPE_POINT = 0,
        /// <summary>A spot light.</summary>
        PARTICLE_LIGHT_TYPE_SPOT = 1,
        /// <summary>An effects light, diffuse only and without shadows.</summary>
        PARTICLE_LIGHT_TYPE_FX = 2,
        /// <summary>A capsule light.</summary>
        PARTICLE_LIGHT_TYPE_CAPSULE = 3,
    }
}

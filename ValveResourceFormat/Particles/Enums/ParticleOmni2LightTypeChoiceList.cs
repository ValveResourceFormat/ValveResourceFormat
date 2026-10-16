namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Particle Omni2 light type choice list.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOmni2LightTypeChoiceList_t">ParticleOmni2LightTypeChoiceList_t</seealso>
    public enum ParticleOmni2LightTypeChoiceList
    {
        /// <summary>Omni2 light with a point luminaire shape.</summary>
        PARTICLE_OMNI2_LIGHT_TYPE_POINT = 0,
        /// <summary>Omni2 light with a sphere luminaire shape.</summary>
        PARTICLE_OMNI2_LIGHT_TYPE_SPHERE = 1,
        /// <summary>Omni2 light with a barn door shape, not rendered yet and treated as a point light.</summary>
        PARTICLE_OMNI2_LIGHT_TYPE_BARN = 2,
    }
}

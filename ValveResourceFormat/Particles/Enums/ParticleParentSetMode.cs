namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Which system in the tree a control point write goes to.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleParentSetMode_t">ParticleParentSetMode_t</seealso>
    public enum ParticleParentSetMode
    {
        /// <summary>The system running the function.</summary>
        PARTICLE_SET_PARENT_NO = 0,
        /// <summary>Its parent, or the system itself when it has none.</summary>
        PARTICLE_SET_PARENT_IMMEDIATE = 1,
        /// <summary>The root of the tree.</summary>
        PARTICLE_SET_PARENT_ROOT = 2,
    }
}

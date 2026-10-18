namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Which particle a function picks out of the system.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleSelection_t">ParticleSelection_t</seealso>
    public enum ParticleSelection
    {
        /// <summary>The particle the given number of places from the start.</summary>
        PARTICLE_SELECTION_FIRST = 0,
        /// <summary>The particle the given number of places from the end.</summary>
        PARTICLE_SELECTION_LAST = 1,
        /// <summary>The particle at the given index, or none for a negative one.</summary>
        PARTICLE_SELECTION_NUMBER = 2,
    }
}

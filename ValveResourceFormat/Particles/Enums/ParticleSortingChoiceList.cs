namespace ValveResourceFormat.Particles
{
    /// <summary>The order a renderer draws its particles in.</summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleSortingChoiceList_t">ParticleSortingChoiceList_t</seealso>
    public enum ParticleSortingChoiceList
    {
        /// <summary>By distance from the camera, nearest drawn last.</summary>
        PARTICLE_SORTING_NEAREST = 0,
        /// <summary>By age.</summary>
        PARTICLE_SORTING_CREATION_TIME = 1,
    }
}

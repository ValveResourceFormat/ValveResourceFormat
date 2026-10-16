namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// What a particle inheriting from a parent particle does once that parent is gone.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/MissingParentInheritBehavior_t">MissingParentInheritBehavior_t</seealso>
    public enum ParticleMissingParentBehavior
    {
        /// <summary>Keeps the value it last inherited.</summary>
        MISSING_PARENT_DO_NOTHING = -1,
        /// <summary>Kills the particle.</summary>
        MISSING_PARENT_KILL = 0,
        /// <summary>Binds to a parent particle picked at random.</summary>
        MISSING_PARENT_FIND_NEW = 1,
        /// <summary>Binds to the parent particle now at the same index, or the nearest index that exists.</summary>
        MISSING_PARENT_SAME_INDEX = 2,
    }
}

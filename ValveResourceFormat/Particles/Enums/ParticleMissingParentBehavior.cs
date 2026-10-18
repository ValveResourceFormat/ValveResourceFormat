namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// What inheritance does when a particle's parent link does not resolve
    /// </summary>
    public enum ParticleMissingParentBehavior
    {
        /// <summary>Kills the particle when no parent is available</summary>
        MISSING_PARENT_KILL = 0,
        /// <summary>Picks a new parent row</summary>
        MISSING_PARENT_FIND_NEW = 1,
    }
}

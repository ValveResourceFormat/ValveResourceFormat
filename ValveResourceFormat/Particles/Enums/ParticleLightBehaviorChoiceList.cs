namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a capsule particle light lays out its axis.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleLightBehaviorChoiceList_t">ParticleLightBehaviorChoiceList_t</seealso>
    public enum ParticleLightBehaviorChoiceList
    {
        /// <summary>Along the particle's direction, at a fixed length.</summary>
        PARTICLE_LIGHT_BEHAVIOR_FOLLOW_DIRECTION = 0,
        /// <summary>From each particle to the next, as a chain.</summary>
        PARTICLE_LIGHT_BEHAVIOR_ROPE = 1,
        /// <summary>Back toward the particle's previous position, sized like a trail.</summary>
        PARTICLE_LIGHT_BEHAVIOR_TRAILS = 2,
    }
}

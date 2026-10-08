namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// A position on an entity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleEntityPos_t">ParticleEntityPos_t</seealso>
    public enum ParticleEntityPos
    {
        /// <summary>The entity origin, which is at a player's feet.</summary>
        PARTICLE_ABS_ORIGIN = 0,

        /// <summary>The middle of the entity bounds.</summary>
        PARTICLE_WORLDSPACE_CENTER = 1,

        /// <summary>The eyes.</summary>
        PARTICLE_EYES = 2,

        /// <summary>The flashlight.</summary>
        PARTICLE_FLASHLIGHT = 3,
    }
}

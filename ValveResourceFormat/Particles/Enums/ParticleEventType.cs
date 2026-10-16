namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Particle events a system can hand to its children, which emit from them.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/EventTypeSelection_t">EventTypeSelection_t</seealso>
    [Flags]
    public enum ParticleEventType
    {
        /// <summary>No event.</summary>
        PARTICLE_EVENT_TYPE_MASK_NONE = 0,
        /// <summary>Particles were created with a positive lifetime.</summary>
        PARTICLE_EVENT_TYPE_MASK_SPAWNED = 1,
        /// <summary>Particles were removed.</summary>
        PARTICLE_EVENT_TYPE_MASK_KILLED = 2,
        /// <summary>Particles hit the world.</summary>
        PARTICLE_EVENT_TYPE_MASK_COLLISION = 4,
        /// <summary>Particles hit the world for the first time.</summary>
        PARTICLE_EVENT_TYPE_MASK_FIRST_COLLISION = 8,
        /// <summary>Particles came to rest against the world.</summary>
        PARTICLE_EVENT_TYPE_MASK_COLLISION_STOPPED = 16,
        /// <summary>Particles were removed by hitting the world.</summary>
        PARTICLE_EVENT_TYPE_MASK_KILLED_ON_COLLISION = 32,
        /// <summary>First user event.</summary>
        PARTICLE_EVENT_TYPE_MASK_USER_1 = 64,
        /// <summary>Second user event.</summary>
        PARTICLE_EVENT_TYPE_MASK_USER_2 = 128,
        /// <summary>Third user event.</summary>
        PARTICLE_EVENT_TYPE_MASK_USER_3 = 256,
        /// <summary>Fourth user event.</summary>
        PARTICLE_EVENT_TYPE_MASK_USER_4 = 512,
        /// <summary>Particles were removed by a cull operator.</summary>
        PARTICLE_EVENT_TYPE_MASK_KILLED_ON_CULL = 1024,
        /// <summary>Particles were created with no lifetime left.</summary>
        PARTICLE_EVENT_TYPE_MASK_CULLED_ON_SPAWN = 2048,
    }
}

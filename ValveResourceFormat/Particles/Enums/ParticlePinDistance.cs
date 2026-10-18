namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// What releases a particle pinned to a control point.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticlePinDistance_t">ParticlePinDistance_t</seealso>
    public enum ParticlePinDistance
    {
        /// <summary>Nothing; the particle stays pinned.</summary>
        PARTICLE_PIN_DISTANCE_NONE = -1,
        /// <summary>Stretch of the distance to the particle next to the pinned one.</summary>
        PARTICLE_PIN_DISTANCE_NEIGHBOR = 0,
        /// <summary>Stretch of the distance to the particle at the far end of the system.</summary>
        PARTICLE_PIN_DISTANCE_FARTHEST = 1,
        /// <summary>Stretch of the distance to the first particle.</summary>
        PARTICLE_PIN_DISTANCE_FIRST = 2,
        /// <summary>Stretch of the distance to the last particle.</summary>
        PARTICLE_PIN_DISTANCE_LAST = 3,
        /// <summary>Stretch of the distance to the centre of the system's bounds.</summary>
        PARTICLE_PIN_DISTANCE_CENTER = 5,
        /// <summary>Stretch of the distance from the pin control point to the break control point.</summary>
        PARTICLE_PIN_DISTANCE_CP = 6,
        /// <summary>Stretch of the larger distance from the pin control point to the two break control points.</summary>
        PARTICLE_PIN_DISTANCE_CP_PAIR_EITHER = 7,
        /// <summary>Stretch of the smaller distance from the pin control point to the two break control points.</summary>
        PARTICLE_PIN_DISTANCE_CP_PAIR_BOTH = 8,
        /// <summary>Speed of the pin control point.</summary>
        PARTICLE_PIN_SPEED = 9,
        /// <summary>Age of the system.</summary>
        PARTICLE_PIN_COLLECTION_AGE = 10,
        /// <summary>A value reaching 1.</summary>
        PARTICLE_PIN_FLOAT_VALUE = 11,
    }
}

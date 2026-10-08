namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a world collision constraint finds the surfaces particles collide with.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleCollisionMode_t">ParticleCollisionMode_t</seealso>
    public enum ParticleCollisionMode
    {
        /// <summary>No collision.</summary>
        COLLISION_MODE_DISABLED = -1,
        /// <summary>A single trace down at spawn.</summary>
        COLLISION_MODE_INITIAL_TRACE_DOWN = 0,
        /// <summary>A cached set of planes refreshed every frame.</summary>
        COLLISION_MODE_PER_FRAME_PLANESET = 1,
        /// <summary>Reuses the nearest cached trace.</summary>
        COLLISION_MODE_USE_NEAREST_TRACE = 2,
        /// <summary>A trace per particle per step.</summary>
        COLLISION_MODE_PER_PARTICLE_TRACE = 3,
    }
}

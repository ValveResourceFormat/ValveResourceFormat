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
        /// <summary>One trace straight down from the control point, made once, giving a single plane.</summary>
        COLLISION_MODE_INITIAL_TRACE_DOWN = 0,
        /// <summary>A set of planes traced in 26 directions around the control point, retraced when it moves.</summary>
        COLLISION_MODE_PER_FRAME_PLANESET = 1,
        /// <summary>Collides the same way as <see cref="COLLISION_MODE_PER_FRAME_PLANESET"/>.</summary>
        COLLISION_MODE_USE_NEAREST_TRACE = 2,
        /// <summary>A trace of each particle's movement every step.</summary>
        COLLISION_MODE_PER_PARTICLE_TRACE = 3,
    }
}

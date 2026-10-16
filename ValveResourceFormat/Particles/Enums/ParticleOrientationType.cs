namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// How a control point placed on a particle takes its orientation from the particle.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOrientationType_t">ParticleOrientationType_t</seealso>
#pragma warning disable CA1027
    public enum ParticleOrientationType
    {
        /// <summary>The control point keeps its orientation.</summary>
        PARTICLE_ORIENTATION_NONE = 0,
        /// <summary>Faces along the particle's velocity.</summary>
        PARTICLE_ORIENTATION_VELOCITY = 1,
        /// <summary>Faces along the particle's normal.</summary>
        PARTICLE_ORIENTATION_NORMAL = 2,
        /// <summary>Takes the particle's pitch, yaw and roll.</summary>
        PARTICLE_ORIENTATION_ROTATION = 4,
    }
#pragma warning restore CA1027
}

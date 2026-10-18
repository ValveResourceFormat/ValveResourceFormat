namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Where a control point placed on a particle takes its orientation from.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOrientationSetMode_t">ParticleOrientationSetMode_t</seealso>
    public enum ParticleOrientationSetMode
    {
        /// <summary>The control point keeps its orientation.</summary>
        PARTICLE_ORIENTATION_SET_NONE = -1,
        /// <summary>Faces along the particle's velocity.</summary>
        PARTICLE_ORIENTATION_SET_FROM_VELOCITY = 0,
        /// <summary>Faces along the particle's normal.</summary>
        PARTICLE_ORIENTATION_SET_FROM_NORMAL = 1,
        /// <summary>Takes the particle's pitch, yaw and roll.</summary>
        PARTICLE_ORIENTATION_SET_FROM_ROTATIONS = 2,
    }
}

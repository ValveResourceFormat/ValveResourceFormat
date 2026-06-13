namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle orientation modes for rendering.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOrientationChoiceList_t">ParticleOrientationChoiceList_t</seealso>
    public enum ParticleOrientation
    {
        /// <summary>Particle always faces the screen.</summary>
        PARTICLE_ORIENTATION_SCREEN_ALIGNED = 0,
        /// <summary>Particle is aligned to the screen's Z axis.</summary>
        PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED = 1,
        /// <summary>Particle is aligned to the world's Z axis.</summary>
        PARTICLE_ORIENTATION_WORLD_Z_ALIGNED = 2,
        /// <summary>Particle is aligned to the particle's normal vector.</summary>
        PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL = 3,
        /// <summary>Particle faces the screen but is tilted toward the particle's normal vector.</summary>
        PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL = 4,
        /// <summary>Particle uses full 3-axis rotation from its rotation fields.</summary>
        PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION = 5,
    }
}

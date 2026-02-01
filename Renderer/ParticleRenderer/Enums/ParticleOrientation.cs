namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle orientation modes for rendering.
    /// </summary>
    public enum ParticleOrientation // ParticleOrientationChoiceList_t
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        PARTICLE_ORIENTATION_SCREEN_ALIGNED = 0,
        PARTICLE_ORIENTATION_SCREEN_Z_ALIGNED = 1,
        PARTICLE_ORIENTATION_WORLD_Z_ALIGNED = 2,
        PARTICLE_ORIENTATION_ALIGN_TO_PARTICLE_NORMAL = 3,
        PARTICLE_ORIENTATION_SCREENALIGN_TO_PARTICLE_NORMAL = 4,
        PARTICLE_ORIENTATION_FULL_3AXIS_ROTATION = 5,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Methods for modifying particle attributes.
    /// </summary>
    public enum ParticleSetMethod
    {
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
        PARTICLE_SET_REPLACE_VALUE = 0,
        PARTICLE_SET_SCALE_INITIAL_VALUE = 1,
        PARTICLE_SET_ADD_TO_INITIAL_VALUE = 2,
        PARTICLE_SET_RAMP_CURRENT_VALUE = 3,
        PARTICLE_SET_SCALE_CURRENT_VALUE = 4,
        PARTICLE_SET_ADD_TO_CURRENT_VALUE = 5,
#pragma warning restore CS1591 // Missing XML comment for publicly visible type or member
    }
}

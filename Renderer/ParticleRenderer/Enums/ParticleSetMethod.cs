namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Methods for modifying particle attributes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleSetMethod_t">ParticleSetMethod_t</seealso>
    public enum ParticleSetMethod
    {
        /// <summary>Replaces the current attribute value with the new value.</summary>
        PARTICLE_SET_REPLACE_VALUE = 0,
        /// <summary>Multiplies the initial spawn-time attribute value by the new value.</summary>
        PARTICLE_SET_SCALE_INITIAL_VALUE = 1,
        /// <summary>Adds the new value to the initial spawn-time attribute value.</summary>
        PARTICLE_SET_ADD_TO_INITIAL_VALUE = 2,
        /// <summary>Adds the new value multiplied by age to the current attribute value (exponential ramp).</summary>
        PARTICLE_SET_RAMP_CURRENT_VALUE = 3,
        /// <summary>Multiplies the current attribute value by the new value.</summary>
        PARTICLE_SET_SCALE_CURRENT_VALUE = 4,
        /// <summary>Adds the new value to the current attribute value.</summary>
        PARTICLE_SET_ADD_TO_CURRENT_VALUE = 5,
    }
}

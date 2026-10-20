namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Which way an Omni2 particle light faces.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleOmni2LighOrientationChoiceList_t">ParticleOmni2LighOrientationChoiceList_t</seealso>
    public enum ParticleOmni2LightOrientationChoiceList
    {
        /// <summary>Faces along the particle's rotation angles.</summary>
        PARTICLE_OMNI2_LIGHT_ORIENTATION_ROTATIONS = 0,
        /// <summary>Faces along the normal.</summary>
        PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL = 1,
        /// <summary>Faces along the normal, rolled by the particle's roll.</summary>
        PARTICLE_OMNI2_LIGHT_ORIENTATION_NORMAL_ROLL = 2,
        /// <summary>Faces toward the target.</summary>
        PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET = 3,
        /// <summary>Faces toward the target, rolled by the particle's roll.</summary>
        PARTICLE_OMNI2_LIGHT_ORIENTATION_TARGET_ROLL = 4,
    }
}

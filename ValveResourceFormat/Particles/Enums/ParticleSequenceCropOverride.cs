namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Whether a renderer shrinks its cards to the art of their sheet frames. Only a first texture layer
    /// with a sheet is ever cropped.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleSequenceCropOverride_t">ParticleSequenceCropOverride_t</seealso>
    public enum ParticleSequenceCropOverride
    {
        /// <summary>Cropped when every texture layer past the first is a colour lookup.</summary>
        PARTICLE_SEQUENCE_CROP_OVERRIDE_DEFAULT = -1,
        /// <summary>Never cropped.</summary>
        PARTICLE_SEQUENCE_CROP_OVERRIDE_FORCE_OFF = 0,
        /// <summary>Cropped whatever the other layers are.</summary>
        PARTICLE_SEQUENCE_CROP_OVERRIDE_FORCE_ON = 1,
    }
}

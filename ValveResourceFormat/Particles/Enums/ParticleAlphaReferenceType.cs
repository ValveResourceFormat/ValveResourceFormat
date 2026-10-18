namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// Which alpha an alpha tested particle keeps once it passes the test: the first half names the
    /// texture alpha, the second the vertex alpha, each either kept or replaced by full opacity.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/ParticleAlphaReferenceType_t">ParticleAlphaReferenceType_t</seealso>
    public enum ParticleAlphaReferenceType
    {
        /// <summary>Keeps both the texture and the vertex alpha.</summary>
        PARTICLE_ALPHA_REFERENCE_ALPHA_ALPHA = 0,
        /// <summary>Draws the texture opaque and keeps the vertex alpha.</summary>
        PARTICLE_ALPHA_REFERENCE_OPAQUE_ALPHA = 1,
        /// <summary>Keeps the texture alpha and draws the vertex alpha opaque.</summary>
        PARTICLE_ALPHA_REFERENCE_ALPHA_OPAQUE = 2,
        /// <summary>Draws both opaque.</summary>
        PARTICLE_ALPHA_REFERENCE_OPAQUE_OPAQUE = 3,
    }
}

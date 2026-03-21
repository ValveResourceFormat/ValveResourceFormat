namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Particle animation timing modes.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/AnimationType_t">AnimationType_t</seealso>
    public enum ParticleAnimationType
    {
        /// <summary>Animation advances at a fixed rate regardless of lifetime.</summary>
        ANIMATION_TYPE_FIXED_RATE,
        /// <summary>Animation frame is set manually by operators.</summary>
        ANIMATION_TYPE_MANUAL_FRAMES,
        /// <summary>Animation is stretched to fit the particle lifetime.</summary>
        ANIMATION_TYPE_FIT_LIFETIME,
    }
}

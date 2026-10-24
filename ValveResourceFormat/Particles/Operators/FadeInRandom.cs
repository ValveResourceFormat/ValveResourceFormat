namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a per-particle randomly chosen duration drawn from a min/max range with an optional exponent bias.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade In Random" in the particle editor. Unlike "Alpha Fade In Simple", the range
    /// can be defined in seconds rather than a fraction of the lifespan by turning proportional off.
    /// The fade stops once the particle's age at the start of a step, in seconds, reaches the fade
    /// time, also when the fade is proportional. A proportional fade therefore only completes for
    /// lifetimes up to a second; a longer-lived particle keeps the alpha it had reached.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeIn">C_OP_FadeIn</seealso>
    class FadeInRandom : CGeneralRandomFade
    {
        public FadeInRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeInTime")
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (var particle in particles.Current)
            {
                var fadeInTime = GetFadeTime(particle, particleSystemState);

                if (MathF.Max(0f, particle.Age - frameTime) < fadeInTime)
                {
                    var time = proportional
                        ? MathUtils.Saturate(particle.NormalizedAge)
                        : particle.Age;

                    particle.Alpha = MathUtils.Smoothstep(0f, fadeInTime, time)
                        * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

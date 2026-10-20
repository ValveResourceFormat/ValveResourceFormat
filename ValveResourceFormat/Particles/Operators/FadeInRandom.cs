namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a per-particle randomly chosen duration drawn from a min/max range with an optional exponent bias.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade In Random" in the particle editor. Unlike "Alpha Fade In Simple", the range
    /// can be defined in seconds rather than a fraction of the lifespan by turning proportional off.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeIn">C_OP_FadeIn</seealso>
    class FadeInRandom : CGeneralRandomFade
    {
        public FadeInRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeInTime")
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var fadeInTime = GetFadeTime(ref particle, particleSystemState);

                var time = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (time < fadeInTime)
                {
                    particle.Alpha = MathUtils.Smoothstep(0f, fadeInTime, time)
                        * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a per-particle randomly chosen duration drawn from a min/max range with an optional exponent bias.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade In Random" in the particle editor. Unlike "Alpha Fade In Simple", the range
    /// can be defined in seconds rather than a fraction of the lifespan by turning proportional off.
    /// </remarks>
    class FadeInRandom : CGeneralRandomFade
    {
        public FadeInRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeInTime")
        {
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var fadeInTime = GetFadeTime(ref particle);

                var time = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (time <= fadeInTime)
                {
                    particle.Alpha = (time / fadeInTime) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

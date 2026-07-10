namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha out over a per-particle randomly chosen duration drawn from a min/max range, with an optional bias curve applied to the fade.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade Out Random" in the particle editor. Unlike "Alpha Fade Out Simple", the range
    /// can be defined in seconds rather than a fraction of the lifespan by turning proportional off.
    /// </remarks>
    class FadeOutRandom : CGeneralRandomFade
    {
        private readonly float fadeBias = 0.5f;

        public FadeOutRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeOutTime")
        {
            var bias = parse.Float("m_flFadeBias", fadeBias);

            if (bias == 0.0f)
            {
                bias = 0.5f;
            }

            fadeBias = bias;

            // Other things that exist that don't seem to do anything:
            // m_bEaseInAndOut
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
            {
                var fadeOutTime = GetFadeTime(ref particle);

                var timeLeft = proportional
                    ? 1.0f - particle.NormalizedAge
                    : particle.Lifetime - particle.Age;

                if (timeLeft <= fadeOutTime)
                {
                    var elapsedFraction = Math.Clamp(1f - timeLeft / fadeOutTime, 0f, 1f);

                    if (fadeBias != 0.5f)
                    {
                        elapsedFraction /= ((1f / fadeBias - 2f) * (1f - elapsedFraction) + 1f);
                    }

                    particle.Alpha = (1f - elapsedFraction) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

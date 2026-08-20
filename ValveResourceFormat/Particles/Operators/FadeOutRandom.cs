using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
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
        /// <summary>The bias curve's parameter. 0.5 is the identity, and an authored 0 means 0.5.</summary>
        private readonly float fadeBias = 0.5f;

        /// <summary>Eases the fade along a smoothstep instead of the bias curve, which it replaces.</summary>
        private readonly bool easeInAndOut = true;

        public FadeOutRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeOutTime")
        {
            var bias = parse.Float("m_flFadeBias", fadeBias);

            if (bias == 0.0f)
            {
                bias = 0.5f;
            }

            fadeBias = bias;
            easeInAndOut = parse.Boolean("m_bEaseInAndOut", easeInAndOut);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                var fadeOutTime = GetFadeTime(ref particle, particleSystemState);

                var timeLeft = proportional
                    ? 1.0f - particle.NormalizedAge
                    : particle.Lifetime - particle.Age;

                if (timeLeft < fadeOutTime)
                {
                    var elapsedFraction = MathUtils.Saturate(1f - timeLeft / fadeOutTime);

                    if (easeInAndOut)
                    {
                        elapsedFraction = MathUtils.Smoothstep(0f, 1f, elapsedFraction);
                    }
                    else if (fadeBias != 0.5f)
                    {
                        elapsedFraction = ParticleMath.Bias(elapsedFraction, fadeBias);
                    }

                    particle.Alpha = (1f - elapsedFraction) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

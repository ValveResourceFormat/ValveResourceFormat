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
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeOut">C_OP_FadeOut</seealso>
    class FadeOutRandom : CGeneralRandomFade
    {
        /// <summary>Eases the fade along a smoothstep rather than running it out linearly.</summary>
        private readonly bool easeInAndOut = true;

        public FadeOutRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeOutTime")
        {
            easeInAndOut = parse.Boolean("m_bEaseInAndOut", easeInAndOut);

            // m_flFadeBias is read but never applied: the variant that carries the bias curve is
            // selected only when the bias is 0.5, the one value at which that curve is the identity.
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

                    particle.Alpha = (1f - elapsedFraction) * particle.GetInitialScalar(particles, ParticleField.Alpha);
                }
            }
        }
    }
}

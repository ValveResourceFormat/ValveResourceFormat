namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a configurable time window and then fades it out, killing the particle at the end of the fade-out.
    /// </summary>
    /// <remarks>
    /// "Alpha Fade and Decay" in the particle editor: essentially combines the operators
    /// "Lifespan Decay", "Alpha Fade In Simple" and "Alpha Fade Out Simple".
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_FadeAndKill">C_OP_FadeAndKill</seealso>
    class FadeAndKill : ParticleFunctionOperator
    {
        private readonly float startFadeInTime;
        private readonly float endFadeInTime = 0.5f;
        private readonly float startFadeOutTime = 0.5f;
        private readonly float endFadeOutTime = 1f;

        private readonly float startAlpha = 1f;
        private readonly float endAlpha;

        public FadeAndKill(ParticleDefinitionParser parse) : base(parse)
        {
            startFadeInTime = parse.Float("m_flStartFadeInTime", startFadeInTime);
            endFadeInTime = parse.Float("m_flEndFadeInTime", endFadeInTime);
            startFadeOutTime = parse.Float("m_flStartFadeOutTime", startFadeOutTime);
            endFadeOutTime = parse.Float("m_flEndFadeOutTime", endFadeOutTime);
            startAlpha = parse.Float("m_flStartAlpha", startAlpha);
            endAlpha = parse.Float("m_flEndAlpha", endAlpha);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            foreach (ref var particle in particles.Current)
            {
                // The particle ends when it outlives its lifetime, which is not the same instant as
                // the end of the fade-out: a fade-out that finishes early leaves it alive and faded
                if (HasExpired(particle.Age, particle.Lifetime))
                {
                    Expire(ref particle, frameTime);
                    continue;
                }

                var time = particle.NormalizedAge;
                var initialAlpha = particle.GetInitialScalar(particles, ParticleField.Alpha);
                var alpha = particle.Alpha;

                if (time >= startFadeInTime && time < endFadeInTime)
                {
                    var blend = MathUtils.Smoothstep(startFadeInTime, endFadeInTime, time);

                    alpha = initialAlpha * float.Lerp(startAlpha, 1f, blend);
                }

                if (time >= startFadeOutTime && time < endFadeOutTime)
                {
                    var blend = MathUtils.Smoothstep(startFadeOutTime, endFadeOutTime, time);

                    alpha = initialAlpha * float.Lerp(1f, endAlpha, blend);
                }

                particle.Alpha = BlendsWithStrength ? float.Lerp(particle.Alpha, alpha, strength) : alpha;
            }
        }

        /// <summary>Whether a particle whose age has exactly reached its lifetime counts as spent.</summary>
        protected virtual bool HasExpired(float age, float lifetime) => lifetime < age;

        /// <summary>Whether the run strength blends the alpha this operator writes.</summary>
        protected virtual bool BlendsWithStrength => true;

        /// <summary>Ends a particle that has outlived its lifetime.</summary>
        protected virtual void Expire(ref Particle particle, float frameTime)
        {
            particle.Kill();
        }
    }
}

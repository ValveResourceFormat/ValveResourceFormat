using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades a particle's alpha in over a configurable time window and then fades it out, killing the particle when its lifetime ends.
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
        private readonly bool forcePreserveParticleOrder;

        public FadeAndKill(ParticleDefinitionParser parse) : base(parse)
        {
            var startIn = parse.Float("m_flStartFadeInTime", startFadeInTime);
            var endIn = parse.Float("m_flEndFadeInTime", endFadeInTime);
            var startOut = parse.Float("m_flStartFadeOutTime", startFadeOutTime);
            var endOut = parse.Float("m_flEndFadeOutTime", endFadeOutTime);

            endIn = MathF.Max(endIn, startIn);
            endOut = MathF.Max(endOut, startOut);

            if (startIn > startOut)
            {
                (startIn, startOut) = (startOut, startIn);
            }

            if (endIn > endOut)
            {
                (endIn, endOut) = (endOut, endIn);
            }

            startFadeInTime = startIn;
            endFadeInTime = endIn;
            startFadeOutTime = startOut;
            endFadeOutTime = endOut;
            startAlpha = parse.Float("m_flStartAlpha", startAlpha);
            endAlpha = parse.Float("m_flEndAlpha", endAlpha);
            forcePreserveParticleOrder = parse.Boolean("m_bForcePreserveParticleOrder", forcePreserveParticleOrder);

            parse.AddInitialRead(ParticleField.Alpha);
        }

        public override bool IsOrderImportant => forcePreserveParticleOrder;

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Alpha.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var ages = current.ReadAges();

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var alphas = current.Floats(ParticleField.Alpha);
            var initialAlphas = particles.Initial.ReadFloats(ParticleField.Alpha);

            for (var i = 0; i < alphas.Length; i++)
            {
                // The particle ends when it outlives its lifetime, which is not the same instant as
                // the end of the fade-out: a fade-out that finishes early leaves it alive and faded
                if (lifetimes[i] < ages[i])
                {
                    particles.Kill(i);
                    continue;
                }

                var time = ParticleMath.NormalizedAge(ages[i], lifetimes[i]);
                var alpha = FadedAlpha(time, initialAlphas[i], alphas[i]);

                alphas[i] = BlendByStrength(alphas[i], alpha, strength);
            }
        }

        /// <summary>
        /// The alpha of a particle at <paramref name="time"/> through its life, inside the fade-out
        /// window or else the fade-in window, and <paramref name="alpha"/> outside both.
        /// </summary>
        protected float FadedAlpha(float time, float initialAlpha, float alpha)
        {
            if (time >= startFadeOutTime && time < endFadeOutTime)
            {
                var fadeOut = MathUtils.Smoothstep(startFadeOutTime, endFadeOutTime, time);
                return initialAlpha * ((fadeOut * (endAlpha - 1f)) + 1f);
            }

            if (time >= startFadeInTime && time < endFadeInTime)
            {
                var fadeIn = MathUtils.Smoothstep(startFadeInTime, endFadeInTime, time);
                return initialAlpha * ((fadeIn * (1f - startAlpha)) + startAlpha);
            }

            return alpha;
        }
    }
}

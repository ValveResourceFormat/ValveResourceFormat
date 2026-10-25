using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Fades alpha out over a per-particle random duration from a min/max range, with optional easing.
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

        public FadeOutRandom(ParticleDefinitionParser parse) : base(parse, "m_flFadeOutTime", randomFromZeroIsInstant: true)
        {
            easeInAndOut = parse.Boolean("m_bEaseInAndOut", easeInAndOut);

            // m_flFadeBias is read but never applied: the variant that carries the bias curve is
            // selected only when the bias is 0.5, the one value at which that curve is the identity.

            parse.AddInitialRead(ParticleField.Alpha);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => ParticleField.Alpha.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var ages = current.ReadAges();
            var fadeTimes = ParticleScratch.Floats(current.Length);
            FillFadeTimes(current, particleSystemState, fadeTimes);

            var lifetimes = current.ReadFloats(ParticleField.LifeDuration);
            var alphas = current.Floats(ParticleField.Alpha);
            var initialAlphas = particles.Initial.ReadFloats(ParticleField.Alpha);

            for (var i = 0; i < alphas.Length; i++)
            {
                var timeLeft = proportional
                    ? 1f - ParticleMath.NormalizedAge(ages[i], lifetimes[i])
                    : lifetimes[i] - ages[i];

                var fading = timeLeft < fadeTimes[i];

                if (!fading)
                {
                    continue;
                }

                var elapsedFraction = MathUtils.Saturate(1f - (timeLeft / fadeTimes[i]));

                if (!easeInAndOut)
                {
                    alphas[i] = (1f - elapsedFraction) * initialAlphas[i];
                    continue;
                }

                var eased = MathUtils.Smoothstep(0f, 1f, elapsedFraction);
                alphas[i] = MathF.Max(0f, (1f - eased) * initialAlphas[i]);
            }
        }
    }
}

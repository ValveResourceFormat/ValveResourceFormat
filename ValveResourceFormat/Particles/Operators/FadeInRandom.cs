using ValveResourceFormat.Particles.Utils;

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
                var age = ages[i];
                var fadeTime = fadeTimes[i];

                // A particle without a positive fade time never fades, which keeps it out of the smoothstep's empty range
                var fading = MathF.Max(0f, age - frameTime) < fadeTime;

                if (!fading)
                {
                    continue;
                }

                var time = proportional
                    ? MathUtils.Saturate(ParticleMath.NormalizedAge(age, lifetimes[i]))
                    : age;

                alphas[i] = MathUtils.Smoothstep(0f, fadeTime, time) * initialAlphas[i];
            }
        }
    }
}

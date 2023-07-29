using System;
using System.Collections.Generic;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeOutRandom : IParticleOperator
    {
        private readonly float fadeOutTimeMin = 0.25f;
        private readonly float fadeOutTimeMax = 0.25f;
        private readonly float randomExponent = 1f;
        private readonly bool proportional = true;

        public FadeOutRandom(ParticleDefinitionParser parse)
        {
            fadeOutTimeMin = parse.Float("m_flFadeOutTimeMin", fadeOutTimeMin);

            fadeOutTimeMax = parse.Float("m_flFadeOutTimeMax", fadeOutTimeMax);

            randomExponent = parse.Float("m_flFadeOutTimeExp", randomExponent);

            proportional = parse.Boolean("m_bProportional", proportional);

            // Other things that exist that don't seem to do anything:
            // m_bEaseInAndOut
            // m_flFadeBias
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // TODO: Consistent rng
                var fadeOutTime = MathUtils.RandomWithExponentBetween(randomExponent, fadeOutTimeMin, fadeOutTimeMax);

                var timeLeft = proportional
                    ? 1.0f - particle.NormalizedAge
                    : particle.Lifetime - particle.Age;

                if (timeLeft <= fadeOutTime)
                {
                    var newAlpha = (timeLeft / fadeOutTime) * particle.InitialAlpha;
                    particle.Alpha = newAlpha;
                }
            }
        }
    }
}

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

        public FadeOutRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeOutTimeMin"))
            {
                fadeOutTimeMin = keyValues.GetFloatProperty("m_flFadeOutTimeMin");
            }

            if (keyValues.ContainsKey("m_flFadeOutTimeMax"))
            {
                fadeOutTimeMax = keyValues.GetFloatProperty("m_flFadeOutTimeMax");
            }

            if (keyValues.ContainsKey("m_flFadeOutTimeExp"))
            {
                randomExponent = keyValues.GetFloatProperty("m_flFadeOutTimeExp");
            }

            if (keyValues.ContainsKey("m_bProportional"))
            {
                proportional = keyValues.GetProperty<bool>("m_bProportional");
            }

            // Other things that exist that don't seem to do anything:
            // m_bEaseInAndOut
            // m_flFadeBias
        }

        private readonly Dictionary<int, float> FadeOutTimes = new();

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                float fadeOutTime;

                if (!FadeOutTimes.ContainsKey(particle.ParticleCount))
                {
                    FadeOutTimes[particle.ParticleCount] = MathUtils.RandomWithExponentBetween(randomExponent, fadeOutTimeMin, fadeOutTimeMax);
                }

                fadeOutTime = FadeOutTimes[particle.ParticleCount];


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

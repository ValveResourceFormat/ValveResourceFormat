using System;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeInRandom : IParticleOperator
    {
        private readonly float fadeInTimeMin = 0.25f;
        private readonly float fadeInTimeMax = 0.25f;
        private readonly float randomExponent = 1f;
        private readonly bool proportional = true;

        public FadeInRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeInTimeMin"))
            {
                fadeInTimeMin = keyValues.GetFloatProperty("m_flFadeInTimeMin");
            }

            if (keyValues.ContainsKey("m_flFadeInTimeMax"))
            {
                fadeInTimeMax = keyValues.GetFloatProperty("m_flFadeInTimeMax");
            }

            if (keyValues.ContainsKey("m_flFadeInTimeExp"))
            {
                randomExponent = keyValues.GetFloatProperty("m_flFadeInTimeExp");
            }

            if (keyValues.ContainsKey("m_bProportional"))
            {
                proportional = keyValues.GetProperty<bool>("m_bProportional");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                // TODO: Consistent rng
                var fadeInTime = MathUtils.RandomWithExponentBetween(randomExponent, fadeInTimeMin, fadeInTimeMax);

                var time = proportional
                    ? particle.NormalizedAge
                    : particle.Age;

                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particle.InitialAlpha;
                    particle.Alpha = newAlpha;
                }
            }
        }
    }
}

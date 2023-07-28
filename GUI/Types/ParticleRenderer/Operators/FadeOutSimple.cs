using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeOutSimple : IParticleOperator
    {
        private readonly float fadeOutTime = 0.25f;
        private readonly ParticleField fieldOutput = ParticleField.Alpha;

        public FadeOutSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeOutTime"))
            {
                fadeOutTime = keyValues.GetFloatProperty("m_flFadeOutTime");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var timeLeft = 1 - particle.NormalizedAge;
                if (timeLeft <= fadeOutTime)
                {
                    var t = timeLeft / fadeOutTime;
                    var newAlpha = t * particle.InitialAlpha;
                    particle.SetScalar(fieldOutput, newAlpha);
                }
            }
        }
    }
}

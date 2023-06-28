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
            for (var i = 0; i < particles.Length; ++i)
            {
                var timeLeft = 1 - particles[i].NormalizedAge;
                if (timeLeft <= fadeOutTime)
                {
                    var t = timeLeft / fadeOutTime;
                    var newAlpha = t * particles[i].InitialAlpha;
                    particles[i].SetScalar(fieldOutput, newAlpha);
                }
            }
        }
    }
}

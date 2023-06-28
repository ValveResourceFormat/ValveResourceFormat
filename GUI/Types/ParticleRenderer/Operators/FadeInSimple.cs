using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class FadeInSimple : IParticleOperator
    {
        private readonly float fadeInTime = 0.25f;
        private readonly ParticleField fieldOutput = ParticleField.Alpha;

        public FadeInSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flFadeInTime"))
            {
                fadeInTime = keyValues.GetFloatProperty("m_flFadeInTime");
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
                var time = particles[i].NormalizedAge;
                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particles[i].InitialAlpha;
                    particles[i].SetScalar(fieldOutput, newAlpha);
                }
            }
        }
    }
}

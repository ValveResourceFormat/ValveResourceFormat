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
            foreach (ref var particle in particles)
            {
                var time = particle.NormalizedAge;
                if (time <= fadeInTime)
                {
                    var newAlpha = (time / fadeInTime) * particle.InitialAlpha;
                    particle.SetScalar(fieldOutput, newAlpha);
                }
            }
        }
    }
}

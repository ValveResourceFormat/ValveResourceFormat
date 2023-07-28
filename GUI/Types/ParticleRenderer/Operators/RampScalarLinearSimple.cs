using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RampScalarLinearSimple : IParticleOperator
    {
        private readonly float rate;
        private readonly float startTime;
        private readonly float endTime = 1.0f;
        private readonly ParticleField field = ParticleField.Radius;

        public RampScalarLinearSimple(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_Rate"))
            {
                rate = keyValues.GetFloatProperty("m_Rate");
            }

            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEndTime"))
            {
                endTime = keyValues.GetFloatProperty("m_flEndTime");
            }

            if (keyValues.ContainsKey("m_nField"))
            {
                field = keyValues.GetParticleField("m_nField");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                if (particle.Age > startTime && particle.Age < endTime)
                {
                    // Yeah this would change exponentially. Blame valve
                    particle.SetScalar(field, particle.GetScalar(field) + rate * frameTime);
                }
            }
        }
    }
}

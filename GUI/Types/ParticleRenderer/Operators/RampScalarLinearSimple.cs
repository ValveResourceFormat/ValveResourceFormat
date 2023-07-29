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

        public RampScalarLinearSimple(ParticleDefinitionParser parse)
        {
            rate = parse.Float("m_Rate", rate);

            startTime = parse.Float("m_flStartTime", startTime);

            endTime = parse.Float("m_flEndTime", endTime);

            field = parse.ParticleField("m_nField", field);
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

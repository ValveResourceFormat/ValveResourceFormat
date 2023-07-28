using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpScalar : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Radius;
        private readonly INumberProvider output = new LiteralNumberProvider(1);
        private readonly float startTime;
        private readonly float endTime = 1f;

        public LerpScalar(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                field = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flOutput"))
            {
                output = keyValues.GetNumberProvider("m_flOutput");
            }

            if (keyValues.ContainsKey("m_flStartTime"))
            {
                startTime = keyValues.GetFloatProperty("m_flStartTime");
            }

            if (keyValues.ContainsKey("m_flEndTime"))
            {
                endTime = keyValues.GetFloatProperty("m_flEndTime");
            }
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var lerpTarget = output.NextNumber(ref particle, particleSystemState);

                var lerpWeight = MathUtils.Saturate(MathUtils.Remap(particle.Age, startTime, endTime));

                var scalarOutput = MathUtils.Lerp(lerpWeight, particle.GetInitialScalar(field), lerpTarget);

                particle.SetScalar(field, scalarOutput);
            }
        }
    }
}

using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class QuantizeFloat : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);

        public QuantizeFloat(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_nInputValue"))
            {
                quantizeSize = keyValues.GetNumberProvider("m_nInputValue");
            }
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var quantizeSize = this.quantizeSize.NextNumber(particle, particleSystemState);
                var value = particle.GetScalar(field);

                if (quantizeSize != 0)
                {
                    value = quantizeSize * MathF.Truncate(value / quantizeSize);
                }

                particle.SetScalar(field, value);
            }
        }
    }
}

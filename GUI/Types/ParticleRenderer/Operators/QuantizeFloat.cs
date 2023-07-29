using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class QuantizeFloat : IParticleOperator
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);

        public QuantizeFloat(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            quantizeSize = parse.NumberProvider("m_nInputValue", quantizeSize);
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var quantizeSize = this.quantizeSize.NextNumber(ref particle, particleSystemState);
                var value = particle.GetScalar(OutputField);

                if (quantizeSize != 0)
                {
                    value = quantizeSize * MathF.Truncate(value / quantizeSize);
                }

                particle.SetScalar(OutputField, value);
            }
        }
    }
}

using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class ClampScalar : IParticleOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField OutputField = ParticleField.Radius;

        public ClampScalar(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);

            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);

            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var min = outputMin.NextNumber(ref particle, particleSystemState);
                var max = outputMax.NextNumber(ref particle, particleSystemState);
                MathUtils.MinMaxFixUp(ref min, ref max);

                var clampedValue = Math.Clamp(particle.GetScalar(OutputField), min, max);
                particle.SetScalar(OutputField, clampedValue);
            }
        }
    }
}

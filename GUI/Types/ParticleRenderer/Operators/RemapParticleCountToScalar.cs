using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class OpRemapParticleCountToScalar : IParticleOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;

        public OpRemapParticleCountToScalar(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            inputMin = parse.NumberProvider("m_flInputMin", inputMin);
            inputMax = parse.NumberProvider("m_flInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        // is this particle id or total particle count?
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var inputMin = this.inputMin.NextNumber(ref particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(ref particle, particleSystemState);

                if (activeRange && (particle.ParticleCount < inputMin || particle.ParticleCount > inputMax))
                {
                    continue;
                }

                var remappedDistance = MathUtils.Remap(particle.ParticleCount, inputMin, inputMax);

                remappedDistance = MathUtils.Saturate(remappedDistance);

                var outputMin = this.outputMin.NextNumber(ref particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(ref particle, particleSystemState);

                var finalValue = MathUtils.Lerp(remappedDistance, outputMin, outputMax);

                finalValue = particle.ModifyScalarBySetMethod(OutputField, finalValue, setMethod);

                particle.SetScalar(OutputField, finalValue);
            }
        }
    }
}

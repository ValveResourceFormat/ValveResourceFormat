using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class RemapSpeed : IParticleOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField field = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public RemapSpeed(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_flInputMin"))
            {
                inputMin = keyValues.GetNumberProvider("m_flInputMin");
            }

            if (keyValues.ContainsKey("m_flInputMax"))
            {
                inputMax = keyValues.GetNumberProvider("m_flInputMax");
            }

            if (keyValues.ContainsKey("m_flOutputMin"))
            {
                outputMin = keyValues.GetNumberProvider("m_flOutputMin");
            }

            if (keyValues.ContainsKey("m_flOutputMax"))
            {
                outputMax = keyValues.GetNumberProvider("m_flOutputMax");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (var particle in particles)
            {
                var inputMin = this.inputMin.NextNumber(particle, particleSystemState);
                var inputMax = this.inputMax.NextNumber(particle, particleSystemState);

                var remappedDistance = MathUtils.Remap(particle.Speed, inputMin, inputMax);

                remappedDistance = MathUtils.Saturate(remappedDistance);

                var outputMin = this.outputMin.NextNumber(particle, particleSystemState);
                var outputMax = this.outputMax.NextNumber(particle, particleSystemState);

                var finalValue = MathUtils.Lerp(remappedDistance, outputMin, outputMax);

                finalValue = particle.ModifyScalarBySetMethod(field, finalValue, setMethod);

                particle.SetScalar(field, finalValue);
            }
        }
    }
}

using GUI.Utils;
using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetVec : IParticleOperator
    {
        private readonly ParticleField OutputField = ParticleField.Color;
        private readonly IVectorProvider value = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetVec(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);

            value = parse.VectorProvider("m_nInputValue", value);

            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            lerp = parse.NumberProvider("m_Lerp", lerp);

            // there's also a Lerp value that will fade it in when at low values. Further testing is needed to know anything more
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var value = this.value.NextVector(ref particle, particleSystemState);
                var lerp = this.lerp.NextNumber(ref particle, particleSystemState);

                var currentValue = particle.ModifyVectorBySetMethod(OutputField, value, setMethod);
                var initialValue = particle.GetVector(OutputField);

                value = MathUtils.Lerp(lerp, initialValue, currentValue);

                particle.SetVector(OutputField, value);
            }
        }
    }
}

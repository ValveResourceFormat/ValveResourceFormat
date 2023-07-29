using GUI.Utils;
using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetFloat : IParticleOperator
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetFloat(ParticleDefinitionParser parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);

            value = parse.NumberProvider("m_nInputValue", value);

            if (parse.Data.ContainsKey("m_nSetMethod"))
            {
                setMethod = parse.Data.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }

            lerp = parse.NumberProvider("m_Lerp", lerp);

            // there's also a Lerp value that every frame sets the value to the lerp of the current one to the set one.
            // Thus it's basically like exponential decay, except it works with the
            // initial value, which works because they store the init value
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var value = this.value.NextNumber(ref particle, particleSystemState);
                var lerp = this.lerp.NextNumber(ref particle, particleSystemState);

                var currentValue = particle.ModifyScalarBySetMethod(OutputField, value, setMethod);
                var initialValue = particle.GetScalar(OutputField);

                value = MathUtils.Lerp(lerp, initialValue, currentValue);

                particle.SetScalar(OutputField, value);
            }
        }
    }
}

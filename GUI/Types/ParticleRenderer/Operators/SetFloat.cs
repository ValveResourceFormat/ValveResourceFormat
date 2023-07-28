using GUI.Utils;
using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetFloat : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetFloat(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_nInputValue"))
            {
                value = keyValues.GetNumberProvider("m_nInputValue");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }

            if (keyValues.ContainsKey("m_Lerp"))
            {
                lerp = keyValues.GetNumberProvider("m_Lerp");
            }

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

                var currentValue = particle.ModifyScalarBySetMethod(field, value, setMethod);
                var initialValue = particle.GetScalar(field);

                value = MathUtils.Lerp(lerp, initialValue, currentValue);

                particle.SetScalar(field, value);
            }
        }
    }
}

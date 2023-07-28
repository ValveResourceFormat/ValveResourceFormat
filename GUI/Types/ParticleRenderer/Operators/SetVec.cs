using GUI.Utils;
using System;
using System.Numerics;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetVec : IParticleOperator
    {
        private readonly ParticleField field = ParticleField.Color;
        private readonly IVectorProvider value = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetVec(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                field = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_nInputValue"))
            {
                value = keyValues.GetVectorProvider("m_nInputValue");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }

            if (keyValues.ContainsKey("m_Lerp"))
            {
                lerp = keyValues.GetNumberProvider("m_Lerp");
            }

            // there's also a Lerp value that will fade it in when at low values. Further testing is needed to know anything more
        }
        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var value = this.value.NextVector(ref particle, particleSystemState);
                var lerp = this.lerp.NextNumber(ref particle, particleSystemState);

                var currentValue = particle.ModifyVectorBySetMethod(field, value, setMethod);
                var initialValue = particle.GetVector(field);

                value = MathUtils.Lerp(lerp, initialValue, currentValue);

                particle.SetVector(field, value);
            }
        }
    }
}

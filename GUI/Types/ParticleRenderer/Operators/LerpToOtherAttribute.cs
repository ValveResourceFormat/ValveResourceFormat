using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class LerpToOtherAttribute : IParticleOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Color;
        private readonly ParticleField fieldOutput = ParticleField.Color;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1.0f);

        private readonly bool skip;
        public LerpToOtherAttribute(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldInput"))
            {
                fieldInput = keyValues.GetParticleField("m_nFieldInput");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_flInterpolation"))
            {
                interpolation = keyValues.GetNumberProvider("m_flInterpolation");
            }

            // If the two fields are different types, the operator does nothing.
            skip = fieldInput.FieldType() != fieldOutput.FieldType();
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            // We don't have to do weird stuff with this one because it doesn't have the option to set the initial.
            if (!skip)
            {
                if (fieldInput.FieldType() == "vector")
                {
                    foreach (var particle in particles)
                    {
                        var interp = interpolation.NextNumber(particle, particleSystemState);
                        var blend = MathUtils.Lerp(interp, particle.GetVector(fieldOutput), particle.GetVector(fieldInput));
                        particle.SetVector(fieldOutput, blend);
                    }
                }
                else if (fieldInput.FieldType() == "float")
                {
                    foreach (var particle in particles)
                    {
                        var interp = interpolation.NextNumber(particle, particleSystemState);
                        var blend = MathUtils.Lerp(interp, particle.GetScalar(fieldOutput), particle.GetScalar(fieldInput));
                        particle.SetScalar(fieldOutput, blend);
                    }
                }
            }
        }
    }
}

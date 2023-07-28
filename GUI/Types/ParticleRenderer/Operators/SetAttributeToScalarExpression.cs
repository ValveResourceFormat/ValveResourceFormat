using System;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetAttributeToScalarExpression : IParticleOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0);
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public SetAttributeToScalarExpression(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nOutputField"))
            {
                outputField = keyValues.GetParticleField("m_nOutputField");
            }

            if (keyValues.ContainsKey("m_flInput1"))
            {
                input1 = keyValues.GetNumberProvider("m_flInput1");
            }

            if (keyValues.ContainsKey("m_flInput2"))
            {
                input2 = keyValues.GetNumberProvider("m_flInput2");
            }

            if (keyValues.ContainsKey("m_nExpression"))
            {
                expression = keyValues.GetEnumValue<ScalarExpressionType>("m_nExpression");
            }

            if (keyValues.ContainsKey("m_nSetMethod"))
            {
                setMethod = keyValues.GetEnumValue<ParticleSetMethod>("m_nSetMethod");
            }
        }

        public void Update(Span<Particle> particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles)
            {
                var value1 = input1.NextNumber(ref particle, particleSystemState);
                var value2 = input2.NextNumber(ref particle, particleSystemState);

                var output = expression switch
                {
                    ScalarExpressionType.SCALAR_EXPRESSION_UNINITIALIZED
                        => 0f,
                    ScalarExpressionType.SCALAR_EXPRESSION_ADD
                        => value1 + value2,
                    ScalarExpressionType.SCALAR_EXPRESSION_SUBTRACT
                        => value1 - value2,
                    ScalarExpressionType.SCALAR_EXPRESSION_MUL
                        => value1 * value2,
                    ScalarExpressionType.SCALAR_EXPRESSION_DIVIDE
                        => value1 / value2,
                    ScalarExpressionType.SCALAR_EXPRESSION_INPUT_1
                        => value1,
                    ScalarExpressionType.SCALAR_EXPRESSION_MIN
                        => Math.Min(value1, value2),
                    ScalarExpressionType.SCALAR_EXPRESSION_MAX
                        => Math.Max(value1, value2),
                    ScalarExpressionType.SCALAR_EXPRESSION_MOD // new in CS2
                        => (float)(value1 % value2),
                    _ => throw new NotImplementedException($"Unrecognized scalar expression type ({expression})")
                };

                particle.SetScalar(outputField, particle.ModifyScalarBySetMethod(outputField, output, setMethod));
            }
        }
    }
}

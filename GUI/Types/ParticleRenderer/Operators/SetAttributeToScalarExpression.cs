using ValveResourceFormat;

namespace GUI.Types.ParticleRenderer.Operators
{
    class SetAttributeToScalarExpression : ParticleFunctionOperator
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0);
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public SetAttributeToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            input1 = parse.NumberProvider("m_flInput1", input1);
            input2 = parse.NumberProvider("m_flInput2", input2);
            expression = parse.Enum<ScalarExpressionType>("m_nExpression", expression);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState)
        {
            foreach (ref var particle in particles.Current)
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

                particle.SetScalar(OutputField, particle.ModifyScalarBySetMethod(particles, OutputField, output, setMethod));
            }
        }
    }
}

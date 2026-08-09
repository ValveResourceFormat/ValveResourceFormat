namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Evaluates a scalar expression (add, subtract, multiply, divide, min, max, mod, input passthrough,
    /// or comparisons such as equal/greater-than/less-than) on two per-particle float inputs and writes
    /// the result to a scalar particle attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetAttributeToScalarExpression">C_OP_SetAttributeToScalarExpression</seealso>
    class SetAttributeToScalarExpression : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0);
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public SetAttributeToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            input1 = parse.NumberProvider("m_flInput1", input1);
            input2 = parse.NumberProvider("m_flInput2", input2);
            expression = parse.Enum<ScalarExpressionType>("m_nExpression", expression);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
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
                        => value2 == 0f ? 0f : value1 / value2,
                    ScalarExpressionType.SCALAR_EXPRESSION_INPUT_1
                        => value1,
                    ScalarExpressionType.SCALAR_EXPRESSION_MIN
                        => Math.Min(value1, value2),
                    ScalarExpressionType.SCALAR_EXPRESSION_MAX
                        => Math.Max(value1, value2),
                    ScalarExpressionType.SCALAR_EXPRESSION_MOD // new in CS2
                        => (float)(value1 % value2),
                    ScalarExpressionType.SCALAR_EXPRESSION_EQUAL
                        => value1 == value2 ? 1f : 0f,
                    ScalarExpressionType.SCALAR_EXPRESSION_GT
                        => value1 > value2 ? 1f : 0f,
                    ScalarExpressionType.SCALAR_EXPRESSION_LT
                        => value1 < value2 ? 1f : 0f,
                    _ => throw new NotImplementedException($"Unrecognized scalar expression type ({expression})")
                };

                // Strength scales the raw arithmetic result before the set method combines it;
                // the comparison expressions emit their bare 0/1 unscaled
                if (expression is not (ScalarExpressionType.SCALAR_EXPRESSION_EQUAL
                    or ScalarExpressionType.SCALAR_EXPRESSION_GT
                    or ScalarExpressionType.SCALAR_EXPRESSION_LT))
                {
                    output *= strength;
                }

                particle.SetScalar(outputField, particle.ModifyScalarBySetMethod(particles, outputField, output, setMethod));
            }
        }
    }
}

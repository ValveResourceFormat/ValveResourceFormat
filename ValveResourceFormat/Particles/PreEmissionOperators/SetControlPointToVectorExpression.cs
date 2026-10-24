namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Evaluates a binary vector expression (add, subtract, multiply, divide, cross product, etc.)
    /// on two input vectors and writes the result to a control point.
    ///
    /// <para>Division is component-wise and takes no special path for a zero divisor, so it writes an
    /// infinity or a NaN into the control point. The scalar expression operators substitute zero.</para>
    ///
    /// <para>An unknown expression writes zero. <c>m_flLerp</c> is read once per step, from the first
    /// particle when it depends on one.</para>
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointToVectorExpression">C_OP_SetControlPointToVectorExpression</seealso>
    class SetControlPointToVectorExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly int outputCP = 2;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider lerp = new LiteralNumberProvider(0f);
        private readonly VectorExpression expression = VectorExpression.VECTOR_EXPRESSION_ADD;
        private readonly bool normalizedOutput;

        public SetControlPointToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            lerp = parse.NumberProvider("m_flLerp", lerp);
            expression = parse.Enum<VectorExpression>("m_nExpression", expression);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var vec1 = input1.NextVector(particleSystemState);
            var vec2 = input2.NextVector(particleSystemState);
            var lerpAmount = particleSystemState.Data is { Particles.Count: > 0 } simulation
                ? lerp.NextNumber(simulation.Particles.Current[0], particleSystemState)
                : lerp.NextNumber(particleSystemState);

            var output = expression switch
            {
                VectorExpression.VECTOR_EXPRESSION_ADD => vec1 + vec2,
                VectorExpression.VECTOR_EXPRESSION_SUBTRACT => vec1 - vec2,
                VectorExpression.VECTOR_EXPRESSION_MUL => vec1 * vec2,
                VectorExpression.VECTOR_EXPRESSION_DIVIDE => vec1 / vec2,
                VectorExpression.VECTOR_EXPRESSION_INPUT_1 => vec1,
                VectorExpression.VECTOR_EXPRESSION_MIN => Vector3.Min(vec1, vec2),
                VectorExpression.VECTOR_EXPRESSION_MAX => Vector3.Max(vec1, vec2),
                VectorExpression.VECTOR_EXPRESSION_CROSSPRODUCT => Vector3.Cross(vec1, vec2),
                VectorExpression.VECTOR_EXPRESSION_LERP => MathUtils.Lerp(vec1, vec2, lerpAmount),
                _ => Vector3.Zero,
            };

            if (normalizedOutput)
            {
                output = MathUtils.SafeNormalize(output);
            }

            particleSystemState.SetControlPointValue(outputCP, output);
        }
    }
}

using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Combines two collection float inputs with a scalar expression, maps the result through the
    /// output remap and writes it to one component of a control point's position, moved there from the
    /// current value by the interpolation input.
    /// </summary>
    /// <remarks>
    /// An unknown expression writes 0, and the operator strength is ignored. A component outside 0 to 2
    /// is not written.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFieldToScalarExpression">C_OP_SetControlPointFieldToScalarExpression</seealso>
    class SetControlPointFieldToScalarExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0f);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0f);
        private readonly AttributeMapping? outputRemap;
        private readonly int outputCP = 2;
        private readonly int outVectorField;
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);

        public SetControlPointFieldToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.CollectionNumberProvider("m_flInput1", input1);
            input2 = parse.CollectionNumberProvider("m_flInput2", input2);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            outVectorField = parse.Int32("m_nOutVectorField", outVectorField);
            interpolation = parse.CollectionNumberProvider("m_flInterpolation", interpolation);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var value = expression.Evaluate(input1.NextNumber(particleSystemState), input2.NextNumber(particleSystemState)) ?? 0f;

            if (outputRemap != null)
            {
                value = outputRemap.ApplyMapping(value);
            }

            if (outVectorField is < 0 or > 2)
            {
                return;
            }

            var point = particleSystemState.GetControlPoint(outputCP);
            var amount = interpolation.NextNumber(particleSystemState);

            if (amount != 1f)
            {
                value = MathUtils.RemapValClamped(amount, 0f, 1f, point.Position.GetComponent(outVectorField), value);
            }

            point.SetComponent(outVectorField, value);
        }
    }
}

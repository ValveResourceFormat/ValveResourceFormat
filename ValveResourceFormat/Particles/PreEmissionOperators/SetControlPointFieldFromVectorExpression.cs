using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Reduces two collection vector inputs to a number (dot product, distance, length or noise),
    /// scales it by the operator strength, maps it through the output remap and writes it to one
    /// component of a control point's position.
    /// </summary>
    /// <remarks>
    /// An unknown expression writes the remap of 0, and <c>m_flLerp</c> has no effect. A component
    /// outside 0 to 2 is not written.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetControlPointFieldFromVectorExpression">C_OP_SetControlPointFieldFromVectorExpression</seealso>
    class SetControlPointFieldFromVectorExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly VectorFloatExpressionType expression = VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DOTPRODUCT;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly AttributeMapping? outputRemap;
        private readonly int outputCP = 2;
        private readonly int outVectorField;

        public SetControlPointFieldFromVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.CollectionVectorProvider("m_vecInput1", input1);
            input2 = parse.CollectionVectorProvider("m_vecInput2", input2);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
            outputCP = parse.Int32("m_nOutputCP", outputCP);
            outVectorField = parse.Int32("m_nOutVectorField", outVectorField);
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime, float strength)
        {
            var value1 = input1.NextVector(particleSystemState);
            var value2 = input2.NextVector(particleSystemState);
            var value = expression.Evaluate(value1, value2) * strength ?? 0f;

            if (outputRemap != null)
            {
                value = outputRemap.ApplyMapping(value);
            }

            if (outVectorField is < 0 or > 2)
            {
                return;
            }

            particleSystemState.SetControlPointValueComponent(outputCP, outVectorField, value);
        }
    }
}

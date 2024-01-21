namespace GUI.Types.ParticleRenderer.PreEmissionOperators
{
    class SetControlPointToVectorExpression : ParticleFunctionPreEmissionOperator
    {
        private readonly int OutputCP = 1;
        private readonly IVectorProvider Input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider Input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly VectorExpression Expression = VectorExpression.VECTOR_EXPRESSION_ADD;

        public SetControlPointToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            OutputCP = parse.Int32("m_nOutputCP", OutputCP);
            Input1 = parse.VectorProvider("m_vInput1", Input1);
            Input2 = parse.VectorProvider("m_vInput2", Input2);
            Expression = parse.Enum<VectorExpression>("m_nExpression", Expression);
        }

        public override void Operate(ref ParticleSystemRenderState particleSystemState, float frameTime)
        {
            var vec1 = Input1.NextVector(particleSystemState);
            var vec2 = Input2.NextVector(particleSystemState);

            var output = Expression switch
            {
                VectorExpression.VECTOR_EXPRESSION_UNINITIALIZED => Vector3.Zero,
                VectorExpression.VECTOR_EXPRESSION_ADD => vec1 + vec2,
                VectorExpression.VECTOR_EXPRESSION_SUBTRACT => vec1 - vec2,
                VectorExpression.VECTOR_EXPRESSION_MUL => vec1 * vec2,
                VectorExpression.VECTOR_EXPRESSION_DIVIDE => vec1 / vec2,
                VectorExpression.VECTOR_EXPRESSION_INPUT_1 => vec1,
                VectorExpression.VECTOR_EXPRESSION_MIN => Vector3.Min(vec1, vec2),
                VectorExpression.VECTOR_EXPRESSION_MAX => Vector3.Max(vec1, vec2),
                VectorExpression.VECTOR_EXPRESSION_CROSSPRODUCT => Vector3.Cross(vec1, vec2),
                // "VECTOR_EXPRESSION_NORMALIZE_INPUT_1" // Not in latest dota version
                _ => throw new NotImplementedException($"Unrecognized vector expression type ({Expression})")
            };

            particleSystemState.SetControlPointValue(OutputCP, output);
        }
    }
}

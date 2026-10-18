using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    class SetVectorAttributeToVectorExpression : ParticleFunctionInitializer
    {
        private readonly VectorExpression expression = VectorExpression.VECTOR_EXPRESSION_ADD;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider lerp = new LiteralNumberProvider(0f);
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool normalizedOutput;

        public SetVectorAttributeToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum<VectorExpression>("m_nExpression", expression);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            lerp = parse.NumberProvider("m_flLerp", lerp);
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            Vector3 output;
            if (expression == VectorExpression.VECTOR_EXPRESSION_UNINITIALIZED)
            {
                output = particle.GetVector(outputField);
            }
            else
            {
                var vec1 = input1.NextVector(ref particle, particleSystemState);
                var vec2 = input2.NextVector(ref particle, particleSystemState);
                output = VectorExpressionEvaluator.Evaluate(expression, vec1, vec2, lerp.NextNumber(ref particle, particleSystemState), operatorPath: false);
            }

            output = particle.ModifyVectorBySetMethodAtSpawn(particles, outputField, output, setMethod);

            if (outputField.IsNormalizedField())
            {
                output = Vector3.Clamp(output, Vector3.Zero, Vector3.One);
            }

            if (normalizedOutput)
            {
                output = MathUtils.SafeNormalize(output);
            }

            particle.SetVector(outputField, output);

            return particle;
        }
    }
}

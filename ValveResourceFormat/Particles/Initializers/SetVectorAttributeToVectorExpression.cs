using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Combines two per-particle vector inputs with a vector expression and writes the result to a
    /// vector attribute by a set method. A colour or alpha output is clamped to [0, 1], and the
    /// result can be normalized.
    /// </summary>
    /// <remarks>
    /// The lerp expression clamps <c>m_flLerp</c> to [0, 1] only for the whole blocks of four of an
    /// emission of 16 or more particles, as <see cref="ParticleFunctionInitializer.SplitIntoCalls"/>
    /// splits it, and takes it as is everywhere else.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetVectorAttributeToVectorExpression">C_INIT_SetVectorAttributeToVectorExpression</seealso>
    class SetVectorAttributeToVectorExpression : ParticleFunctionInitializer
    {
        private readonly VectorExpressionType expression = VectorExpressionType.VECTOR_EXPRESSION_ADD;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider lerp = new LiteralNumberProvider(0f);
        private readonly ParticleField outputField = ParticleField.Normal;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool normalizedOutput;

        private bool clampLerp;

        public SetVectorAttributeToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            lerp = parse.NumberProvider("m_flLerp", lerp);
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            InitializeEach(batch[head], particles, particleSystemState);
            clampLerp = true;
            InitializeEach(batch[blocks], particles, particleSystemState);
            clampLerp = false;
            InitializeEach(batch[tail], particles, particleSystemState);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var value1 = input1.NextVector(ref particle, particleSystemState);
            var value2 = input2.NextVector(ref particle, particleSystemState);
            var lerpAmount = lerp.NextNumber(ref particle, particleSystemState);

            if (expression.Evaluate(value1, value2, lerpAmount, clampLerp) is not { } result)
            {
                return particle;
            }

            var value = particle.ModifyVectorBySetMethodAtSpawn(particles, outputField, result, setMethod);

            if (outputField.IsNormalizedField())
            {
                value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
            }

            if (normalizedOutput)
            {
                value = ParticleExpressions.Normalize(value);
            }

            particle.SetVector(outputField, value);

            return particle;
        }
    }
}

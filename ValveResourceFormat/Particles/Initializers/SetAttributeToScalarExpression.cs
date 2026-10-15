using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Combines two per-particle float inputs with a scalar expression, maps the result through the
    /// output remap and writes it to a float attribute by a set method, held inside the attribute's
    /// range.
    /// </summary>
    /// <remarks>
    /// An emission of 16 or more particles also recomputes the attribute of every live particle, with
    /// the set method applied on top of its current value. The new particles before the first index
    /// that is a multiple of four, and those from the last such index on, are written once more.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetAttributeToScalarExpression">C_INIT_SetAttributeToScalarExpression</seealso>
    class SetAttributeToScalarExpression : ParticleFunctionInitializer
    {
        private readonly ScalarExpressionType expression = ScalarExpressionType.SCALAR_EXPRESSION_ADD;
        private readonly INumberProvider input1 = new LiteralNumberProvider(0f);
        private readonly INumberProvider input2 = new LiteralNumberProvider(0f);
        private readonly AttributeMapping? outputRemap;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public SetAttributeToScalarExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.NumberProvider("m_flInput1", input1);
            input2 = parse.NumberProvider("m_flInput2", input2);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override void Initialize(Span<Particle> batch, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var (head, blocks, tail) = SplitIntoCalls(batch);

            base.Initialize(batch[head], particles, particleSystemState);

            if (!batch[blocks].IsEmpty)
            {
                base.Initialize(particles.Current, particles, particleSystemState);
            }

            base.Initialize(batch[tail], particles, particleSystemState);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var value1 = input1.NextNumber(ref particle, particleSystemState);
            var value2 = input2.NextNumber(ref particle, particleSystemState);

            if (expression.Evaluate(value1, value2) is not { } value)
            {
                return particle;
            }

            if (outputRemap != null)
            {
                value = outputRemap.ApplyMapping(value);
            }

            var (min, max) = outputField.SetFloatRange();
            particle.SetScalar(outputField, Math.Clamp(particle.ModifyScalarBySetMethodAtSpawn(particles, outputField, value, setMethod), min, max));

            return particle;
        }
    }
}

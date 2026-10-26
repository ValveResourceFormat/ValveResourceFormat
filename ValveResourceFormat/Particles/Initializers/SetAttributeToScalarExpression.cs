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

        public override ulong WrittenFields => outputField.Mask();

        /// <inheritdoc/>
        public override ulong LiveWrittenFields => outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particles, particleSystemState, blockRun: true);
        }

        private void InitializeRun(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState, bool blockRun)
        {
            var target = blockRun ? particles.Current : run;
            var values1 = input1.Fill(target, particleSystemState);
            var values2 = input2.Fill(target, particleSystemState);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var (min, max) = outputField.SetFloatRange();
            var outputs = target.Floats(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            for (var i = 0; i < outputs.Length; i++)
            {
                var current = outputs[i];

                // An unknown expression passes the current value on as the result
                var value = expression.Evaluate(values1[i], values2[i], truncatedRemainder: blockRun) ?? current;

                if (outputRemap != null)
                {
                    value = outputRemap.ApplyMapping(value);
                }

                var result = ParticleFieldExtensions.ApplySetMethod(value, current, current, deltaTime, setMethod);
                outputs[i] = Math.Clamp(result, min, max);
            }
        }
    }
}

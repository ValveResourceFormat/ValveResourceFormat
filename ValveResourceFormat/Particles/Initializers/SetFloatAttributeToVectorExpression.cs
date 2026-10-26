using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Reduces two per-particle vector inputs to a number (dot product, distance, length or noise)
    /// and writes it to a float attribute by a set method, then through the output remap.
    /// </summary>
    /// <remarks>
    /// An angle output takes the number as degrees, unless the set method scales the value. An alpha
    /// output is clamped to [0, 1] before the remap rather than after it. An emission of 16 or more
    /// particles also recomputes the attribute of every live particle, with the set method applied on
    /// top of its current value.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_SetFloatAttributeToVectorExpression">C_INIT_SetFloatAttributeToVectorExpression</seealso>
    class SetFloatAttributeToVectorExpression : ParticleFunctionInitializer
    {
        private readonly VectorFloatExpressionType expression = VectorFloatExpressionType.VECTOR_FLOAT_EXPRESSION_DOTPRODUCT;
        private readonly IVectorProvider input1 = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider input2 = new LiteralVectorProvider(Vector3.Zero);
        private readonly AttributeMapping? outputRemap;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly float outputScale = 1f;

        public SetFloatAttributeToVectorExpression(ParticleDefinitionParser parse) : base(parse)
        {
            expression = parse.Enum("m_nExpression", expression);
            input1 = parse.VectorProvider("m_vInput1", input1);
            input2 = parse.VectorProvider("m_vInput2", input2);
            outputRemap = AttributeMapping.FromRemapInput(parse, "m_flOutputRemap");
            outputField = parse.ParticleField("m_nOutputField", outputField);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            outputScale = ParticleExpressions.AngleOutputScale(outputField, setMethod);
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
            // Particle attribute inputs are read from the first particle rather than from the first block of the run.
            // In whole blocks outputs are written from the first block while inputs are read from the run's first block,
            // shifting them against the outputs of the live particles; each particle keeps its own inputs here
            var target = blockRun ? particles.Current : run;
            target.FillVectorInputs(input1, input2, particleSystemState, out var values1, out var values2);

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var outputs = target.Floats(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            var clampsToUnit = outputField.IsAlpha();

            for (var i = 0; i < outputs.Length; i++)
            {
                var current = outputs[i];

                // An unknown expression passes the current value on, or in whole blocks the previous block's output, unscaled
                var fallback = blockRun ? ParticleExpressions.PreviousBlockResult(outputs, i) : current;
                var result = expression.Evaluate(values1[i], values2[i]) * outputScale ?? fallback;
                // Whole blocks read the initial value for the initial set methods, but new particles only get theirs
                // after every initializer has run, so it would be stale; the current value is used instead
                var value = ParticleFieldExtensions.ApplySetMethod(result, current, current, deltaTime, setMethod);

                if (clampsToUnit)
                {
                    value = MathUtils.Saturate(value);
                }

                if (outputRemap != null)
                {
                    value = outputRemap.ApplyMapping(value);
                }

                outputs[i] = value;
            }
        }
    }
}

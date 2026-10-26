using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Combines two per-particle vector inputs with a vector expression and writes the result to a
    /// vector attribute by a set method. A colour or alpha output is clamped to [0, 1], and the
    /// result can be normalized.
    /// </summary>
    /// <remarks>
    /// The whole blocks of four of an emission of 16 or more particles, as
    /// <see cref="ParticleFunctionInitializer.SplitIntoCalls"/> splits it, clamp <c>m_flLerp</c> to
    /// [0, 1], keep a zero divisor component off zero, and normalize without an epsilon. Everywhere
    /// else the lerp is taken as is, the division is plain, and the normalize adds an epsilon to the
    /// squared length.
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

        public override ulong WrittenFields => outputField.Mask();

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
            var values1 = input1.Prepare(run, particleSystemState);
            var values2 = input2.Prepare(run, particleSystemState);

            // The lerp is evaluated for every live particle, and each particle of the run reads its own value
            // rather than the value of the particle as far into the collection as it is into the run
            var lerps = lerp.Fill(run, particles.Current, particleSystemState);

            foreach (var (offset, block, lane) in run.InputBlocks())
            {
                input1.FillPerBlock(block, lane, particleSystemState, values1[offset..]);
                input2.FillPerBlock(block, lane, particleSystemState, values2[offset..]);
            }

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var outputs = run.Vectors(outputField);
            var deltaTime = particleSystemState.CurrentStep;
            var clampsToUnit = outputField.IsNormalizedField();

            for (var i = 0; i < outputs.Length; i++)
            {
                var current = outputs[i];

                // An unknown expression passes the current value on, or in whole blocks the previous block's output
                var fallback = blockRun ? ParticleExpressions.PreviousBlockResult(outputs, i) : current;
                var result = expression.Evaluate(values1[i], values2[i], lerps[i], blockRun) ?? fallback;
                var value = ParticleFieldExtensions.ApplySetMethod(result, current, current, deltaTime, setMethod);

                if (clampsToUnit)
                {
                    value = Vector3.Clamp(value, Vector3.Zero, Vector3.One);
                }

                if (normalizedOutput)
                {
                    value = blockRun ? NormalizeBlock(value) : ParticleMath.NormalizeWithEpsilon(value);
                }

                outputs[i] = value;
            }
        }

        /// <summary>
        /// Scales <paramref name="value"/> to unit length, taking a squared length below
        /// <see cref="ParticleMath.FloatMin"/> as <see cref="ParticleMath.FloatEpsilon"/> so a zero vector stays zero.
        /// </summary>
        private static Vector3 NormalizeBlock(Vector3 value)
        {
            var lengthSquared = value.LengthSquared();

            if (lengthSquared < ParticleMath.FloatMin)
            {
                lengthSquared = ParticleMath.FloatEpsilon;
            }

            return value / MathF.Sqrt(lengthSquared);
        }
    }
}

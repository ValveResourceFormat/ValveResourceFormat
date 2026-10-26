namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Sets a scalar particle attribute from a collection-wide value and interpolation factor, so
    /// every particle moves toward the same target each frame.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="SetFloat"/>, not that operator with a wider input.
    /// Strength multiplies both the value and the interpolation factor rather than the finished result,
    /// so it acts on the outcome twice. Replacing lerps the first four particles toward the value and
    /// copies their results over the rest, so particle <c>i</c> takes the result of particle <c>i % 4</c>.
    /// An angle output is stored as authored, not converted from degrees.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFloatCollection">C_OP_SetFloatCollection</seealso>
    class SetFloatCollection : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider value = new LiteralNumberProvider(0f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);

        public SetFloatCollection(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            value = parse.NumberProvider("m_InputValue", value);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            lerp = parse.NumberProvider("m_Lerp", lerp);

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        /// <inheritdoc/>
        /// <remarks>Evaluates the value once and discards it, so a varying random value takes its draw.</remarks>
        public override void Restart(ParticleSystemState particleSystemState)
        {
            value.NextNumber(particleSystemState);
        }

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var input = value.NextNumber(particleSystemState) * strength;
            var blend = MathUtils.Saturate(lerp.NextNumber(particleSystemState) * strength);
            var (min, max) = outputField.SetFloatRange();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var output = particles.Current.Floats(outputField);

            if (setMethod == ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE)
            {
                Span<float> replacements = stackalloc float[4];

                for (var lane = 0; lane < Math.Min(4, output.Length); lane++)
                {
                    replacements[lane] = Math.Clamp(MathUtils.Lerp(output[lane], input, blend), min, max);
                }

                for (var i = 0; i < output.Length; i++)
                {
                    output[i] = replacements[i & 3];
                }

                return;
            }

            var initial = particles.Initial.ReadFloats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                var current = output[i];

                if (setMethod == ParticleSetMethod.PARTICLE_SET_RAMP_CURRENT_VALUE)
                {
                    output[i] = Math.Clamp(current + (blend * frameTime * input), min, max);
                    continue;
                }

                var target = ParticleFieldExtensions.ApplySetMethod(input, initial[i], current, frameTime, setMethod);
                output[i] = Math.Clamp(MathUtils.Lerp(current, target, blend), min, max);
            }
        }
    }
}

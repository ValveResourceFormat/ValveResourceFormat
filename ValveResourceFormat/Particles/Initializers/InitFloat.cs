namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle attribute to a value provided by a float input. The target field and
    /// the input value are both configurable.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFloat">C_INIT_InitFloat</seealso>
    class InitFloat : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0);
        private readonly INumberProvider inputStrength = new LiteralNumberProvider(1f);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public InitFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
            inputStrength = parse.NumberProvider("m_InputStrength", inputStrength);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        public override ulong WrittenFields => outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particleSystemState, blockRun: false);
        }

        protected override void InitializeBlocks(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            InitializeRun(run, particleSystemState, blockRun: true);
        }

        private void InitializeRun(ParticleSpan run, ParticleSystemState particleSystemState, bool blockRun)
        {
            var values = inputValue.Fill(run, particleSystemState);
            var strengths = inputStrength.Fill(run, particleSystemState);

            if (ParticleBuffer.IsVector(outputField))
            {
                InitializeVectorX(run, values, strengths, particleSystemState);
                return;
            }

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            // Angles are authored in degrees and stored in radians, the same conversion the dedicated
            // rotation initializers do. The scaling set methods take a unitless multiplier, not an angle,
            // so they are left alone.
            var convertAngle = outputField.IsAngleField()
                && setMethod is not (ParticleSetMethod.PARTICLE_SET_SCALE_INITIAL_VALUE or ParticleSetMethod.PARTICLE_SET_SCALE_CURRENT_VALUE);

            var outputs = run.Floats(outputField);
            var integers = outputField.IsIntegerAttribute() ? run.Integers(outputField) : default;

            for (var i = 0; i < outputs.Length; i++)
            {
                var value = convertAngle ? float.DegreesToRadians(values[i]) : values[i];
                var current = outputs[i];
                var target = ParticleFieldExtensions.ApplySetMethod(value, current, current, particleSystemState.CurrentStep, setMethod);

                // The strength blends the attribute toward what the set method produced; it is not a
                // multiplier on the input, so at zero the attribute keeps the value it already had
                var result = MathUtils.Lerp(current, target, strengths[i]);

                if (integers.IsEmpty)
                {
                    outputs[i] = result;
                }
                else
                {
                    // The block run rounds an integer to the nearest, where the particles before and
                    // after it truncate
                    integers[i] = blockRun ? (int)MathF.Round(result) : (int)result;
                }
            }
        }

        /// <summary>A vector output field takes the value in its X component alone.</summary>
        private void InitializeVectorX(ParticleSpan run, ReadOnlySpan<float> values, ReadOnlySpan<float> strengths, ParticleSystemState particleSystemState)
        {
            var outputs = run.Vectors(outputField);

            for (var i = 0; i < outputs.Length; i++)
            {
                var current = outputs[i].X;
                var target = ParticleFieldExtensions.ApplySetMethod(values[i], current, current, particleSystemState.CurrentStep, setMethod);

                outputs[i].X = MathUtils.Lerp(current, target, strengths[i]);
            }
        }
    }
}

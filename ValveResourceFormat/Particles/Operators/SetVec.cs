namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Sets a vector particle attribute to a per-particle vector value, with an optional
    /// interpolation factor that blends between the current value and the target each frame.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetVec">C_OP_SetVec</seealso>
    class SetVec : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly IVectorProvider value = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly INumberProvider lerp = new LiteralNumberProvider(1f);
        private readonly bool normalizedOutput;
        private readonly bool clampsToUnit;

        public SetVec(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            value = parse.VectorProvider("m_InputValue", value);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
            lerp = parse.NumberProvider("m_Lerp", lerp);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
            clampsToUnit = outputField is ParticleField.Color or ParticleField.GlowRgb;

            AddInitialReadOf(parse, setMethod, outputField);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var current = particles.Current;
            var values = value.Prepare(current, particleSystemState);
            var lerps = lerp.Fill(current, particleSystemState);

            current.FillPerBlockInputs(particleSystemState, value, values);

            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var output = current.Vectors(outputField);
            var initial = particles.Initial.ReadVectors(outputField);
            var deltaTime = particleSystemState.CurrentStep;

            for (var i = 0; i < output.Length; i++)
            {
                var lerpAmount = MathUtils.Saturate(lerps[i] * strength);

                var currentValue = output[i];
                var target = ParticleFieldExtensions.ApplySetMethod(values[i], initial[i], currentValue, deltaTime, setMethod);

                if (clampsToUnit)
                {
                    target = Vector3.Clamp(target, Vector3.Zero, Vector3.One);
                }

                var result = Vector3.Lerp(currentValue, target, lerpAmount);

                if (normalizedOutput && result != Vector3.Zero)
                {
                    result = MathUtils.SafeNormalize(result);
                }

                output[i] = result;
            }
        }
    }
}

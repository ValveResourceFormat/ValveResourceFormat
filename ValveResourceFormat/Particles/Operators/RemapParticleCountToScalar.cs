namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's slot in the collection, within a configurable input range, to a scalar
    /// output range and writes the result to a particle attribute.
    /// </summary>
    /// <remarks>
    /// All four bounds are collection scoped, so they are evaluated once for the whole call rather
    /// than per particle. The input bounds address particle slots, so they truncate to whole slots
    /// and the range they select is exclusive on the high side.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapParticleCountToScalar">C_OP_RemapParticleCountToScalar</seealso>
    class RemapParticleCountToScalar : ParticleFunctionOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;

        public RemapParticleCountToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_nInputMin", inputMin);
            inputMax = parse.NumberProvider("m_nInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);

            AddInitialReadOf(parse, setMethod, outputField);
        }

        public override bool IsOrderImportant => true;

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var minimumInput = (int)inputMin.NextNumber(particleSystemState);
            var maximumInput = (int)inputMax.NextNumber(particleSystemState);
            var minimumOutput = outputMin.NextNumber(particleSystemState);
            var maximumOutput = outputMax.NextNumber(particleSystemState);

            if (outputField.IsAlpha())
            {
                minimumOutput = MathUtils.Saturate(minimumOutput);
                maximumOutput = MathUtils.Saturate(maximumOutput);
            }

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var output = particles.Current.Floats(outputField);
            var initial = particles.Initial.ReadFloats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                if (activeRange && (i < minimumInput || i >= maximumInput))
                {
                    continue;
                }

                var finalValue = MathUtils.RemapValClamped(i, minimumInput, maximumInput, minimumOutput, maximumOutput);

                finalValue = ParticleFieldExtensions.ApplySetMethod(finalValue, initial[i], output[i], particleSystemState.CurrentStep, setMethod);

                output[i] = MathUtils.Lerp(output[i], finalValue, strength);
            }
        }
    }
}

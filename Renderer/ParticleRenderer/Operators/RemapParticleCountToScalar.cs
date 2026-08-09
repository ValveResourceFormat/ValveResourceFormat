namespace ValveResourceFormat.Renderer.Particles.Operators
{
    /// <summary>
    /// Remaps each particle's slot in the collection, within a configurable input range, to a scalar
    /// output range and writes the result to a particle attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapParticleCountToScalar">C_OP_RemapParticleCountToScalar</seealso>
    class OpRemapParticleCountToScalar : ParticleFunctionOperator
    {
        private readonly INumberProvider inputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider inputMax = new LiteralNumberProvider(1);
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);

        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool activeRange;

        public OpRemapParticleCountToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.NumberProvider("m_nInputMin", inputMin);
            inputMax = parse.NumberProvider("m_nInputMax", inputMax);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            activeRange = parse.Boolean("m_bActiveRange", activeRange);
            setMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", setMethod);
        }

        /// <summary>
        /// All four bounds are collection scoped, so they are evaluated once for the whole call rather
        /// than per particle. The input bounds address particle slots, so they truncate to whole slots
        /// and the range they select is exclusive on the high side.
        /// </summary>
        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemRenderState particleSystemState, float strength)
        {
            var inputMin = (int)this.inputMin.NextNumber(particleSystemState);
            var inputMax = (int)this.inputMax.NextNumber(particleSystemState);
            var outputMin = this.outputMin.NextNumber(particleSystemState);
            var outputMax = this.outputMax.NextNumber(particleSystemState);

            if (outputField is ParticleField.Alpha or ParticleField.AlphaAlternate)
            {
                outputMin = Math.Clamp(outputMin, 0f, 1f);
                outputMax = Math.Clamp(outputMax, 0f, 1f);
            }

            foreach (ref var particle in particles.Current)
            {
                if (activeRange && (particle.Index < inputMin || particle.Index >= inputMax))
                {
                    continue;
                }

                // A degenerate input range is a threshold, inclusive on the high side
                var finalValue = MathUtils.RemapValClamped(particle.Index, inputMin, inputMax, outputMin, outputMax);

                finalValue = particle.ModifyScalarBySetMethod(particles, outputField, finalValue, setMethod);

                particle.SetScalar(outputField, float.Lerp(particle.GetScalar(outputField), finalValue, strength));
            }
        }
    }
}

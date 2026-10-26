namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Draws a fresh random value for a scalar field on every particle, once, on the frame the endcap
    /// starts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_ReinitializeScalarEndCap">C_OP_ReinitializeScalarEndCap</seealso>
    class ReinitializeScalarEndCap : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        private bool hasRun;

        public ReinitializeScalarEndCap(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.Float("m_flOutputMin", outputMin);
            outputMax = parse.Float("m_flOutputMax", outputMax);

            if (outputField.IsAlpha())
            {
                outputMin = MathUtils.Saturate(outputMin);
                outputMax = MathUtils.Saturate(outputMax);
            }
        }

        public override void Reset()
        {
            hasRun = false;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (hasRun || !particleSystemState.InEndCap)
            {
                return;
            }

            hasRun = true;

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var output = particles.Current.Floats(outputField);

            for (var i = 0; i < output.Length; i++)
            {
                var value = particleSystemState.Random.NextBetween(outputMin, outputMax);

                output[i] = MathUtils.Lerp(output[i], value, strength);
            }
        }
    }
}

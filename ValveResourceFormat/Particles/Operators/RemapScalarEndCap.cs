namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Remaps one scalar field into another, once, on the frame the endcap starts.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapScalarEndCap">C_OP_RemapScalarEndCap</seealso>
    class RemapScalarEndCap : ParticleFunctionOperator
    {
        private readonly ParticleField inputField = ParticleField.Alpha;
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly float inputMin;
        private readonly float inputMax = 1f;
        private readonly float outputMin;
        private readonly float outputMax = 1f;

        private bool hasRun;

        public RemapScalarEndCap(ParticleDefinitionParser parse) : base(parse)
        {
            inputField = parse.ParticleField("m_nFieldInput", inputField);
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            inputMin = parse.Float("m_flInputMin", inputMin);
            inputMax = parse.Float("m_flInputMax", inputMax);
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

            // The output is indexed as a scalar regardless of type, so a vector output gets only its X component overwritten
            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var inputs = current.ReadFloats(inputField);
            var outputs = current.Floats(outputField);

            for (var i = 0; i < outputs.Length; i++)
            {
                var remapped = MathUtils.RemapValClamped(inputs[i], inputMin, inputMax, outputMin, outputMax);

                outputs[i] = MathUtils.Lerp(outputs[i], remapped, strength);
            }
        }
    }
}

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Applies noise to a scalar particle attribute, mapping a noise value into a configurable
    /// output range. Can operate additively or by replacing the current value.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_Noise">C_OP_Noise</seealso>
    class Noise : ParticleFunctionOperator
    {
        private readonly INumberProvider outputMin = new LiteralNumberProvider(0);
        private readonly INumberProvider outputMax = new LiteralNumberProvider(1);
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly bool additive;
        private readonly float noiseScale = 0.1f;
        private readonly float animationTimeScale;

        public Noise(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nFieldOutput", outputField);
            outputMin = parse.NumberProvider("m_flOutputMin", outputMin);
            outputMax = parse.NumberProvider("m_flOutputMax", outputMax);
            additive = parse.Boolean("m_bAdditive", false);
            noiseScale = parse.Float("m_fl4NoiseScale", noiseScale);
            animationTimeScale = parse.Float("m_flNoiseAnimationTimeScale", animationTimeScale);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var min = outputMin.NextNumber(particleSystemState);
            var max = outputMax.NextNumber(particleSystemState);

            if (outputField.IsOrientationField())
            {
                min = float.DegreesToRadians(min);
                max = float.DegreesToRadians(max);
            }

            // Calculate coefficients for noise scaling (noise returns -1..1)
            var valueScale = 0.5f * (max - min);
            var valueBase = min + valueScale;

            if (additive)
            {
                valueScale *= frameTime;
                valueBase *= frameTime;
            }

            // The animation time offset moves the sample point along X only.
            var timeOffset = animationTimeScale * particleSystemState.Age;

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var positions = current.ReadVectors(ParticleField.Position);
            var values = current.Floats(outputField);

            for (var i = 0; i < values.Length; i++)
            {
                var position = positions[i];

                var noise = Utils.Noise.Value3D((position.X * noiseScale) + timeOffset, position.Y * noiseScale, position.Z * noiseScale);
                var value = valueBase + (valueScale * noise);

                values[i] = additive ? values[i] + value : value;
            }
        }
    }
}

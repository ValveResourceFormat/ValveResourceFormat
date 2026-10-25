namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Quantizes a scalar particle attribute onto a grid, snapping it to the nearest multiple of a
    /// given step size. The rotation attributes take their step in degrees.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_QuantizeFloat">C_OP_QuantizeFloat</seealso>
    class QuantizeFloat : ParticleFunctionOperator
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider quantizeSize = new LiteralNumberProvider(0);
        private readonly float sizeScale = 1f;

        public QuantizeFloat(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            quantizeSize = parse.NumberProvider("m_InputValue", quantizeSize);

            // The rotation attributes quantize in degrees; every other field is taken as authored.
            sizeScale = outputField.IsOrientationField()
                ? float.DegreesToRadians(1f)
                : 1f;
        }

        /// <inheritdoc/>
        public override ulong StoredFields => outputField.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var (min, max) = outputField.SetFloatRange();

            if (!ParticleBuffer.IsScalar(outputField))
            {
                return;
            }

            var current = particles.Current;
            var output = current.Floats(outputField);
            var inputs = quantizeSize.Fill(current, particleSystemState);

            for (var i = 0; i < output.Length; i++)
            {
                var size = inputs[i] * strength * sizeScale;
                var value = output[i];

                if (size != 0)
                {
                    value = size * MathF.Floor((value / size) + 0.5f);
                }

                output[i] = Math.Clamp(value, min, max);
            }
        }
    }
}

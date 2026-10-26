namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Copies one component of a vector attribute into a scalar attribute, clamped to [0, 1] for a
    /// normalized output such as alpha.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_RemapVectorComponentToScalar">C_OP_RemapVectorComponentToScalar</seealso>
    class RemapVectorComponentToScalar : ParticleFunctionOperator
    {
        private readonly ParticleField fieldInput = ParticleField.Position;
        private readonly ParticleField fieldOutput = ParticleField.Radius;
        private readonly int component;

        public RemapVectorComponentToScalar(ParticleDefinitionParser parse) : base(parse)
        {
            fieldInput = parse.ParticleField("m_nFieldInput", fieldInput);
            fieldOutput = parse.ParticleField("m_nFieldOutput", fieldOutput);
            component = parse.Int32("m_nComponent", component);
        }

        /// <inheritdoc/>
        public override ulong StoredFields => fieldOutput.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            if (!ParticleBuffer.IsScalar(fieldOutput))
            {
                return;
            }

            var current = particles.Current;
            var output = current.Floats(fieldOutput);

            // A field that is not a vector attribute reads as zero
            if (!ParticleBuffer.IsVector(fieldInput))
            {
                output.Clear();
                return;
            }

            var input = current.ReadVectors(fieldInput);
            var clamp = fieldOutput.IsNormalizedField();

            for (var i = 0; i < output.Length; i++)
            {
                var value = input[i].GetComponent(component);
                output[i] = clamp ? MathUtils.Saturate(value) : value;
            }
        }
    }
}

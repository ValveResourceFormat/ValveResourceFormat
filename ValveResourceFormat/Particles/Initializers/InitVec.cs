namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets, scales, adds to or ramps a vector particle attribute by a vector input, optionally
    /// normalizing the result. The target field and the input value are both configurable.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitVec">C_INIT_InitVec</seealso>
    class InitVec : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly IVectorProvider inputValue = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleSetMethod setMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;
        private readonly bool normalizedOutput;
        private readonly bool writePreviousPosition;

        public InitVec(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.VectorProvider("m_InputValue", inputValue);
            setMethod = parse.Enum("m_nSetMethod", setMethod);
            normalizedOutput = parse.Boolean("m_bNormalizedOutput", normalizedOutput);
            writePreviousPosition = parse.Boolean("m_bWritePreviousPosition", writePreviousPosition);
        }

        public override ulong WrittenFields => writePreviousPosition && outputField == ParticleField.Position
            ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            : outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var inputs = inputValue.Fill(run, particleSystemState);

            // A vector written to bone indices or a scalar lands as raw floats over other storage in the
            // game; it is skipped
            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            var values = run.Vectors(outputField);
            var writesPrevious = writePreviousPosition && outputField == ParticleField.Position;
            var previousPositions = writesPrevious ? run.Vectors(ParticleField.PositionPrevious) : default;

            for (var i = 0; i < values.Length; i++)
            {
                var current = values[i];
                var value = ParticleFieldExtensions.ApplySetMethod(inputs[i], current, current, particleSystemState.CurrentStep, setMethod);

                if (normalizedOutput)
                {
                    // A zero vector stays zero in whole blocks too, rather than becoming NaN
                    value = MathUtils.SafeNormalize(value);
                }

                values[i] = value;

                if (writesPrevious)
                {
                    previousPositions[i] = value;
                }
            }
        }
    }
}

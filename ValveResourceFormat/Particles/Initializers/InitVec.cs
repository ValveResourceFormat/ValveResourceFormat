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
            ? FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious)
            : FieldMask(outputField);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var input = inputValue.NextVector(particle, particleSystemState);
            var value = particle.ModifyVectorBySetMethodAtSpawn(particles, outputField, input, setMethod);

            if (normalizedOutput)
            {
                value = MathUtils.SafeNormalize(value);
            }

            particle.SetVector(outputField, value);

            if (writePreviousPosition && outputField == ParticleField.Position)
            {
                particle.PositionPrevious = value;
            }

            return particle;
        }
    }
}

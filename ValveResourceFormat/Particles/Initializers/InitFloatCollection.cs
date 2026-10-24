namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle attribute to a value read once for the whole collection, so every
    /// particle spawned in a frame receives the same number.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="InitFloat"/>. It carries neither a set method
    /// nor an input strength, so the value always replaces the attribute, and an angle output is
    /// stored as authored rather than converted from degrees. The input is read once for each batch
    /// of new particles, so a random input draws once per batch.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFloatCollection">C_INIT_InitFloatCollection</seealso>
    class InitFloatCollection : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0);

        public InitFloatCollection(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.CollectionNumberProvider("m_InputValue", inputValue);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.SetScalar(outputField, inputValue.NextNumber(particleSystemState));

            return particle;
        }
    }
}

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle attribute to a value read once for the whole collection, so every
    /// particle spawned in a frame receives the same number.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="InitFloat"/>. It carries neither a set method
    /// nor an input strength, so the value always replaces the attribute, and an angle output is
    /// stored as authored rather than converted from degrees.
    ///
    /// <para>The engine reads the input once for a whole batch of new particles. This reads it once
    /// per particle instead, which differs only for an input that does not hold still within a
    /// frame, such as a randomised one.</para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFloatCollection">C_INIT_InitFloatCollection</seealso>
    class InitFloatCollection : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Radius;
        private readonly INumberProvider inputValue = new LiteralNumberProvider(0);

        public InitFloatCollection(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.NumberProvider("m_InputValue", inputValue);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            particle.SetScalar(outputField, inputValue.NextNumber(particleSystemState));

            return particle;
        }
    }
}

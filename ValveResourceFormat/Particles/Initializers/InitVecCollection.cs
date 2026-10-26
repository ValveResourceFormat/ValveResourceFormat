namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a vector particle attribute to a value read once for the whole collection, so every
    /// particle of one initializer call receives the same vector.
    /// </summary>
    /// <remarks>
    /// The collection-scoped counterpart of <see cref="InitVec"/>, with neither a set method nor
    /// clamping: the value always replaces the attribute. A random input draws once per call.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitVecCollection">C_INIT_InitVecCollection</seealso>
    class InitVecCollection : ParticleFunctionInitializer
    {
        private readonly IVectorProvider inputValue = new LiteralVectorProvider(Vector3.Zero);
        private readonly ParticleField outputField = ParticleField.Color;

        public InitVecCollection(ParticleDefinitionParser parse) : base(parse)
        {
            inputValue = parse.CollectionVectorProvider("m_InputValue", inputValue);
            outputField = parse.ParticleField("m_nOutputField", outputField);
        }

        public override ulong WrittenFields => outputField.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var value = inputValue.NextVector(particleSystemState);

            // A vector written to bone indices or a scalar lands as raw floats over other storage in the
            // game; it is skipped
            if (!ParticleBuffer.IsVector(outputField))
            {
                return;
            }

            run.Vectors(outputField).Fill(value);
        }
    }
}

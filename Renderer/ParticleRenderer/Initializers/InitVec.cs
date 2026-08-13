namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a vector particle attribute to a value provided by a vector input. The target field and
    /// the input value are both configurable.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitVec">C_INIT_InitVec</seealso>
    class InitVec : ParticleFunctionInitializer
    {
        private readonly ParticleField outputField = ParticleField.Color;
        private readonly IVectorProvider inputValue = new LiteralVectorProvider(Vector3.Zero);

        public InitVec(ParticleDefinitionParser parse) : base(parse)
        {
            outputField = parse.ParticleField("m_nOutputField", outputField);
            inputValue = parse.VectorProvider("m_InputValue", inputValue);
        }

        public override ulong WrittenFields => FieldMask(outputField);

        // todo: these (operators and initializers) can reference either the current value and the initial value. do we need to store the initial value of all attributes?
        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            particle.SetVector(outputField, inputValue.NextVector(ref particle, particleSystemState));

            return particle;
        }
    }
}

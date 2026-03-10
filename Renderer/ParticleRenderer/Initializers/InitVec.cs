namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a vector particle attribute to a value provided by a vector input. The target field and
    /// the input value are both configurable. Corresponds to <c>C_INIT_InitVec</c>.
    /// </summary>
    class InitVec : ParticleFunctionInitializer
    {
        private readonly ParticleField OutputField = ParticleField.Color;
        private readonly IVectorProvider InputValue = new LiteralVectorProvider(Vector3.Zero);

        public InitVec(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            InputValue = parse.VectorProvider("m_nInputValue", InputValue);
        }

        // todo: these (operators and initializers) can reference either the current value and the initial value. do we need to store the initial value of all attributes?
        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.SetVector(OutputField, InputValue.NextVector(ref particle, particleSystemState));

            return particle;
        }
    }
}

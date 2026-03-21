namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets a scalar particle attribute to a value provided by a float input. The target field and
    /// the input value are both configurable.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFloat">C_INIT_InitFloat</seealso>
    class InitFloat : ParticleFunctionInitializer
    {
        private readonly ParticleField OutputField = ParticleField.Radius;
        private readonly INumberProvider InputValue = new LiteralNumberProvider(0);

        public InitFloat(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            InputValue = parse.NumberProvider("m_InputValue", InputValue);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.SetScalar(OutputField, InputValue.NextNumber(ref particle, particleSystemState));

            return particle;
        }
    }
}

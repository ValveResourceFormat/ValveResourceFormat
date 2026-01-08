namespace ValveResourceFormat.Renderer.Particles.Initializers
{
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

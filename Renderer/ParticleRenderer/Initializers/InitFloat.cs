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
        private readonly INumberProvider InputStrength = new LiteralNumberProvider(1f);
        private readonly ParticleSetMethod SetMethod = ParticleSetMethod.PARTICLE_SET_REPLACE_VALUE;

        public InitFloat(ParticleDefinitionParser parse) : base(parse)
        {
            OutputField = parse.ParticleField("m_nOutputField", OutputField);
            InputValue = parse.NumberProvider("m_InputValue", InputValue);
            InputStrength = parse.NumberProvider("m_InputStrength", InputStrength);
            SetMethod = parse.Enum<ParticleSetMethod>("m_nSetMethod", SetMethod);
        }

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var value = InputValue.NextNumber(ref particle, particleSystemState);
            value *= InputStrength.NextNumber(ref particle, particleSystemState);

            var finalValue = particle.ModifyScalarBySetMethod(particles, OutputField, value, SetMethod);

            particle.SetScalar(OutputField, finalValue);

            return particle;
        }
    }
}

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Hands every new particle to the game as a physics impulse of a radius, magnitude and falloff at
    /// its position. The impulse only acts on game objects, so the particles themselves are left as
    /// they are, and the initializer writes no attribute.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_CreateParticleImpulse">C_INIT_CreateParticleImpulse</seealso>
    class CreateParticleImpulse : ParticleFunctionInitializer
    {
        private readonly INumberProvider inputRadius = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputMagnitude = new LiteralNumberProvider(0f);
        private readonly INumberProvider inputFalloffExp = new LiteralNumberProvider(0.5f);

        public CreateParticleImpulse(ParticleDefinitionParser parse) : base(parse)
        {
            inputRadius = parse.NumberProvider("m_InputRadius", inputRadius);
            inputMagnitude = parse.NumberProvider("m_InputMagnitude", inputMagnitude);
            inputFalloffExp = parse.NumberProvider("m_InputFalloffExp", inputFalloffExp);
        }

        public override ulong WrittenFields => 0;

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            // Read only for the draws a random input takes from the system's sequence
            var values = ParticleScratch.Floats(run.Length);
            inputRadius.Fill(run, particleSystemState, values);
            inputMagnitude.Fill(run, particleSystemState, values);
            inputFalloffExp.Fill(run, particleSystemState, values);
        }
    }
}

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class OffsetVectorToVector : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldInput = ParticleField.Position;
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 OutputMin = Vector3.Zero;
        private readonly Vector3 OutputMax = Vector3.One;

        public OffsetVectorToVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldInput = parse.ParticleField("m_nFieldInput", FieldInput);
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            OutputMin = parse.Vector3("m_vecOutputMin", OutputMin);
            OutputMax = parse.Vector3("m_vecOutputMax", OutputMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var input = particle.GetVector(FieldInput);

            var offset = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, OutputMin, OutputMax);

            particle.SetVector(FieldOutput, input + offset);

            return particle;
        }
    }
}

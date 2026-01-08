namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomVectorComponent : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly float min;
        private readonly float max;
        private readonly int component;

        public RandomVectorComponent(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            min = parse.Float("m_flMin", min);
            max = parse.Float("m_flMax", max);
            component = parse.Int32("m_nComponent", component);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newComponent = ParticleCollection.RandomBetween(particle.ParticleID, min, max);

            particle.SetVectorComponent(FieldOutput, newComponent, component);

            return particle;
        }
    }
}

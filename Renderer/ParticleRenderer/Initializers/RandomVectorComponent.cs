namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes a single component (X, Y, or Z) of a vector particle attribute to a random scalar value between a min and max.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomVectorComponent">C_INIT_RandomVectorComponent</seealso>
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

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var newComponent = ParticleCollection.RandomBetween(particle.ParticleID, min, max);

            particle.SetVectorComponent(FieldOutput, newComponent, component);

            return particle;
        }
    }
}

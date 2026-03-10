namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes a vector particle attribute to a random value with each component chosen independently between a min and max vector.
    /// Corresponds to <c>C_INIT_RandomVector</c>.
    /// </summary>
    class RandomVector : ParticleFunctionInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Position;
        private readonly Vector3 Min;
        private readonly Vector3 Max;

        public RandomVector(ParticleDefinitionParser parse) : base(parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            Min = parse.Vector3("m_vecMin", Min);
            Max = parse.Vector3("m_vecMax", Max);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var newVector = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, Min, Max);

            particle.SetVector(FieldOutput, newVector);

            return particle;
        }
    }
}

namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class VelocityRandom : ParticleFunctionInitializer
    {
        private readonly IVectorProvider vectorMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider vectorMax = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0.1f);

        public VelocityRandom(ParticleDefinitionParser parse) : base(parse)
        {
            vectorMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", vectorMin);
            vectorMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", vectorMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            // A bit unclear what the speed is here, but I do know that going under 1.0 does nothing different than 1.0
            var speedmin = speedMin.NextNumber(ref particle, particleSystemState);
            var speedmax = speedMax.NextNumber(ref particle, particleSystemState);

            var speed = Math.Max(1.0f, ParticleCollection.RandomBetween(particle.ParticleID, speedmin, speedmax));

            var vecMin = vectorMin.NextVector(ref particle, particleSystemState);
            var vecMax = vectorMax.NextVector(ref particle, particleSystemState);

            var velocity = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, vecMin, vecMax);

            particle.Velocity = velocity * speed;

            return particle;
        }
    }
}

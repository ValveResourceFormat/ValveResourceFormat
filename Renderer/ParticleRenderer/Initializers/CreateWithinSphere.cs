namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class CreateWithinSphere : ParticleFunctionInitializer
    {
        protected readonly INumberProvider radiusMin = new LiteralNumberProvider(0);
        protected readonly INumberProvider radiusMax = new LiteralNumberProvider(0);
        protected readonly INumberProvider speedMin = new LiteralNumberProvider(0);
        protected readonly INumberProvider speedMax = new LiteralNumberProvider(0);
        protected readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);
        protected readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        public CreateWithinSphere(ParticleDefinitionParser parse) : base(parse)
        {
            radiusMin = parse.NumberProvider("m_fRadiusMin", radiusMin);
            radiusMax = parse.NumberProvider("m_fRadiusMax", radiusMax);
            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);
            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);
            localCoordinateSystemSpeedMin = parse.VectorProvider("m_LocalCoordinateSystemSpeedMin", localCoordinateSystemSpeedMin);
            localCoordinateSystemSpeedMax = parse.VectorProvider("m_LocalCoordinateSystemSpeedMax", localCoordinateSystemSpeedMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var randomVector = ParticleCollection.RandomBetweenPerComponent(particle.ParticleID, new Vector3(-1), new Vector3(1));

            // Normalize
            var direction = Vector3.Normalize(randomVector);

            var distance = ParticleCollection.RandomBetween(
                particle.ParticleID,
                radiusMin.NextNumber(ref particle, particleSystemState),
                radiusMax.NextNumber(ref particle, particleSystemState));

            var speed = ParticleCollection.RandomBetween(
                particle.ParticleID,
                speedMin.NextNumber(ref particle, particleSystemState),
                speedMax.NextNumber(ref particle, particleSystemState));

            var localCoordinateSystemSpeed = ParticleCollection.RandomBetweenPerComponent(
                particle.ParticleID,
                localCoordinateSystemSpeedMin.NextVector(ref particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(ref particle, particleSystemState));

            particle.Position += direction * distance;
            particle.PositionPrevious = particle.Position;
            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}

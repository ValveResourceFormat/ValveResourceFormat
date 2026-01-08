namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomTrailLength : ParticleFunctionInitializer
    {
        private readonly float minLength = 0.1f;
        private readonly float maxLength = 0.1f;

        public RandomTrailLength(ParticleDefinitionParser parse) : base(parse)
        {
            minLength = parse.Float("m_flMinLength", minLength);
            maxLength = parse.Float("m_flMaxLength", maxLength);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.TrailLength = ParticleCollection.RandomBetween(particle.ParticleID, minLength, maxLength);

            return particle;
        }
    }
}

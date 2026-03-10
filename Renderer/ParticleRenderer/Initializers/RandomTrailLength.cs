namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Initializes the trail length of a particle to a random value between a min and max length.
    /// Corresponds to <c>C_INIT_RandomTrailLength</c>.
    /// </summary>
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

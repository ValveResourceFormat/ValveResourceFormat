namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    class RandomLifeTime : ParticleFunctionInitializer
    {
        private readonly float lifetimeMin;
        private readonly float lifetimeMax;
        private readonly float lifetimeRandomExponent = 1;

        public RandomLifeTime(ParticleDefinitionParser parse) : base(parse)
        {
            lifetimeMin = parse.Float("m_fLifetimeMin", lifetimeMin);
            lifetimeMax = parse.Float("m_fLifetimeMax", lifetimeMax);
            lifetimeMax = parse.Float("m_flLifetimeRandExponent", lifetimeMax);
        }

        public override Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var lifetime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, lifetimeRandomExponent, lifetimeMin, lifetimeMax);

            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

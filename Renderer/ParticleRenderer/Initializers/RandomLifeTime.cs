namespace ValveResourceFormat.Renderer.Particles.Initializers
{
    /// <summary>
    /// Sets the particle lifetime to a random value between a minimum and maximum, with an optional
    /// random exponent to bias the distribution.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_RandomLifeTime">C_INIT_RandomLifeTime</seealso>
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

        public override Particle Initialize(ref Particle particle, ParticleCollection particles, ParticleSystemRenderState particleSystemState)
        {
            var lifetime = ParticleCollection.RandomWithExponentBetween(particle.ParticleID, lifetimeRandomExponent, lifetimeMin, lifetimeMax);

            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

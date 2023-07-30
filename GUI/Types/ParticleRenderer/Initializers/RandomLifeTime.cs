using GUI.Utils;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomLifeTime : IParticleInitializer
    {
        private readonly float lifetimeMin;
        private readonly float lifetimeMax;
        private readonly float lifetimeRandomExponent = 1;

        public RandomLifeTime(ParticleDefinitionParser parse)
        {
            lifetimeMin = parse.Float("m_fLifetimeMin", lifetimeMin);
            lifetimeMax = parse.Float("m_fLifetimeMax", lifetimeMax);
            lifetimeMax = parse.Float("m_flLifetimeRandExponent", lifetimeMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var lifetime = MathUtils.RandomWithExponentBetween(lifetimeRandomExponent, lifetimeMin, lifetimeMax);

            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

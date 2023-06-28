using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomLifeTime : IParticleInitializer
    {
        private readonly float lifetimeMin;
        private readonly float lifetimeMax;
        private readonly float lifetimeRandomExponent = 1;

        public RandomLifeTime(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_fLifetimeMin"))
            {
                lifetimeMin = keyValues.GetFloatProperty("m_fLifetimeMin");
            }

            if (keyValues.ContainsKey("m_fLifetimeMax"))
            {
                lifetimeMax = keyValues.GetFloatProperty("m_fLifetimeMax");
            }

            if (keyValues.ContainsKey("m_flLifetimeRandExponent"))
            {
                lifetimeMax = keyValues.GetFloatProperty("m_flLifetimeRandExponent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var lifetime = MathUtils.RandomWithExponentBetween(lifetimeRandomExponent, lifetimeMin, lifetimeMax);

            particle.InitialLifetime = lifetime;
            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class LifetimeRandom : IParticleInitializer
    {
        private readonly Random random;

        private readonly float lifetimeMin = 0f;
        private readonly float lifetimeMax = 0f;

        public LifetimeRandom(IKeyValueCollection keyValues)
        {
            random = new Random();

            if (keyValues.ContainsKey("m_fLifetimeMin"))
            {
                lifetimeMin = keyValues.GetFloatProperty("m_fLifetimeMin");
            }

            if (keyValues.ContainsKey("m_fLifetimeMax"))
            {
                lifetimeMax = keyValues.GetFloatProperty("m_fLifetimeMax");
            }
        }

        public Particle Initialize(Particle particle)
        {
            var lifetime = lifetimeMin + ((lifetimeMax - lifetimeMin) * (float)random.NextDouble());

            particle.TotalLifetime = lifetime;
            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

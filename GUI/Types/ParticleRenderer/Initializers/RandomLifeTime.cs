using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomLifeTime : IParticleInitializer
    {
        private readonly Random random;

        private readonly float lifetimeMin;
        private readonly float lifetimeMax;

        public RandomLifeTime(IKeyValueCollection keyValues)
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

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemRenderState)
        {
            var lifetime = lifetimeMin + ((lifetimeMax - lifetimeMin) * (float)random.NextDouble());

            particle.ConstantLifetime = lifetime;
            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

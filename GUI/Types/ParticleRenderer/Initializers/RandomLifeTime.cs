using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomLifeTime : IParticleInitializer
    {
        private readonly float lifetimeMin;
        private readonly float lifetimeMax;

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
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var lifetime = lifetimeMin + ((lifetimeMax - lifetimeMin) * (float)Random.Shared.NextDouble());

            particle.ConstantLifetime = lifetime;
            particle.Lifetime = lifetime;

            return particle;
        }
    }
}

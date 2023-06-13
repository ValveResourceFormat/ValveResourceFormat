using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRadius : IParticleInitializer
    {
        private readonly float radiusMin;
        private readonly float radiusMax;

        public RandomRadius(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flRadiusMin"))
            {
                radiusMin = keyValues.GetFloatProperty("m_flRadiusMin");
            }

            if (keyValues.ContainsKey("m_flRadiusMax"))
            {
                radiusMax = keyValues.GetFloatProperty("m_flRadiusMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.ConstantRadius = radiusMin + ((float)Random.Shared.NextDouble() * (radiusMax - radiusMin));
            particle.Radius = particle.ConstantRadius;

            return particle;
        }
    }
}

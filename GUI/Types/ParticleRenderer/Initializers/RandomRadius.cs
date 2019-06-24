using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomRadius : IParticleInitializer
    {
        private readonly float radiusMin;
        private readonly float radiusMax;

        private readonly Random random;

        public RandomRadius(IKeyValueCollection keyValues)
        {
            random = new Random();

            if (keyValues.ContainsKey("m_flRadiusMin"))
            {
                radiusMin = keyValues.GetFloatProperty("m_flRadiusMin");
            }

            if (keyValues.ContainsKey("m_flRadiusMax"))
            {
                radiusMax = keyValues.GetFloatProperty("m_flRadiusMax");
            }
        }

        public Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemRenderState)
        {
            particle.ConstantRadius = radiusMin + ((float)random.NextDouble() * (radiusMax - radiusMin));
            particle.Radius = particle.ConstantRadius;

            return particle;
        }
    }
}

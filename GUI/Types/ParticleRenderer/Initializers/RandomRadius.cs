using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRadius : IParticleInitializer
    {
        private readonly float radiusMin = 1;
        private readonly float radiusMax = 1;
        private readonly float radiusRandomExponent = 1;

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

            if (keyValues.ContainsKey("m_flRadiusRandExponent"))
            {
                radiusRandomExponent = keyValues.GetFloatProperty("m_fLifetimeRandExponent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.InitialRadius = MathUtils.RandomWithExponentBetween(radiusRandomExponent, radiusMin, radiusMax);
            particle.Radius = particle.InitialRadius;

            return particle;
        }
    }
}

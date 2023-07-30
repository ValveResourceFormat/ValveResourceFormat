using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRadius : IParticleInitializer
    {
        private readonly float radiusMin = 1;
        private readonly float radiusMax = 1;
        private readonly float radiusRandomExponent = 1;

        public RandomRadius(ParticleDefinitionParser parse)
        {
            radiusMin = parse.Float("m_flRadiusMin", radiusMin);
            radiusMax = parse.Float("m_flRadiusMax", radiusMax);
            radiusRandomExponent = parse.Float("m_flRadiusRandExponent", radiusRandomExponent);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.Radius = MathUtils.RandomWithExponentBetween(radiusRandomExponent, radiusMin, radiusMax);

            return particle;
        }
    }
}

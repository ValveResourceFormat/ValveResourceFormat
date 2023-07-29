using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomTrailLength : IParticleInitializer
    {
        private readonly float minLength = 0.1f;
        private readonly float maxLength = 0.1f;

        public RandomTrailLength(ParticleDefinitionParser parse)
        {
            minLength = parse.Float("m_flMinLength", minLength);
            maxLength = parse.Float("m_flMaxLength", maxLength);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.TrailLength = MathUtils.RandomBetween(minLength, maxLength);

            return particle;
        }
    }
}

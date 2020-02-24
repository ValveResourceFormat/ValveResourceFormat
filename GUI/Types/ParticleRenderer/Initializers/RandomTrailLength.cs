using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomTrailLength : IParticleInitializer
    {
        private readonly float minLength = 0.1f;
        private readonly float maxLength = 0.1f;

        private readonly Random random = new Random();

        public RandomTrailLength(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flMinLength"))
            {
                minLength = keyValues.GetFloatProperty("m_flMinLength");
            }

            if (keyValues.ContainsKey("m_flMaxLength"))
            {
                maxLength = keyValues.GetFloatProperty("m_flMaxLength");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            particle.TrailLength = minLength + ((float)random.NextDouble() * (maxLength - minLength));

            return particle;
        }
    }
}

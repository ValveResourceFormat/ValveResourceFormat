using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomAlpha : IParticleInitializer
    {
        private readonly int alphaMin = 255;
        private readonly int alphaMax = 255;

        private readonly Random random;

        public RandomAlpha(IKeyValueCollection keyValue)
        {
            random = new Random();

            if (keyValue.ContainsKey("m_nAlphaMin"))
            {
                alphaMin = (int)keyValue.GetIntegerProperty("m_nAlphaMin");
            }

            if (keyValue.ContainsKey("m_nAlphaMax"))
            {
                alphaMax = (int)keyValue.GetIntegerProperty("m_nAlphaMax");
            }
        }

        public Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemRenderState)
        {
            var alpha = random.Next(alphaMin, alphaMax) / 255f;

            particle.ConstantAlpha = alpha;
            particle.Alpha = alpha;

            return particle;
        }
    }
}

using System;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomAlpha : IParticleInitializer
    {
        private readonly int alphaMin = 255;
        private readonly int alphaMax = 255;

        public RandomAlpha(IKeyValueCollection keyValue)
        {
            if (keyValue.ContainsKey("m_nAlphaMin"))
            {
                alphaMin = keyValue.GetInt32Property("m_nAlphaMin");
            }

            if (keyValue.ContainsKey("m_nAlphaMax"))
            {
                alphaMax = keyValue.GetInt32Property("m_nAlphaMax");
            }

            if (alphaMin > alphaMax)
            {
                var temp = alphaMin;
                alphaMin = alphaMax;
                alphaMax = temp;
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var alpha = Random.Shared.Next(alphaMin, alphaMax) / 255f;

            particle.InitialAlpha = alpha;
            particle.Alpha = alpha;

            return particle;
        }
    }
}

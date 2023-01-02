using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class PositionOffset : IParticleInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        private readonly Random random = new();

        public PositionOffset(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_OffsetMin"))
            {
                offsetMin = keyValues.GetVectorProvider("m_OffsetMin");
            }

            if (keyValues.ContainsKey("m_OffsetMax"))
            {
                offsetMax = keyValues.GetVectorProvider("m_OffsetMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var min = offsetMin.NextVector();
            var max = offsetMax.NextVector();

            var distance = min - max;
            var offset = min + (distance * new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));

            particle.Position += offset;

            return particle;
        }
    }
}

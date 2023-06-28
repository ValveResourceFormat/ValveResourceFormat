using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class PositionOffset : IParticleInitializer
    {
        private readonly IVectorProvider offsetMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider offsetMax = new LiteralVectorProvider(Vector3.Zero);

        //private readonly int controlPoint; // unknown what this does

        private readonly bool proportional;

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

            if (keyValues.ContainsKey("m_bProportional"))
            {
                proportional = keyValues.GetProperty<bool>("m_bProportional");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {

            var offset = MathUtils.RandomBetweenPerComponent(
                offsetMin.NextVector(particle, particleSystemState),
                offsetMax.NextVector(particle, particleSystemState));

            if (proportional)
            {
                offset *= particle.Radius;
            }

            particle.Position += offset;

            return particle;
        }
    }
}

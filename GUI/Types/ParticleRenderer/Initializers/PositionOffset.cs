using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class PositionOffset : IParticleInitializer
    {
        private readonly Vector3 offsetMin;
        private readonly Vector3 offsetMax;

        private readonly Random random;

        public PositionOffset(IKeyValueCollection keyValues)
        {
            random = new Random();

            if (keyValues.ContainsKey("m_OffsetMin"))
            {
                var vectorValues = keyValues.GetArray<double>("m_OffsetMin");
                offsetMin = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_OffsetMax"))
            {
                var vectorValues = keyValues.GetArray<double>("m_OffsetMax");
                offsetMax = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }
        }

        public Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemRenderState)
        {
            var distance = offsetMax - offsetMin;
            var offset = offsetMin + (distance * new Vector3((float)random.NextDouble(), (float)random.NextDouble(), (float)random.NextDouble()));

            particle.Position = offset;

            return particle;
        }
    }
}

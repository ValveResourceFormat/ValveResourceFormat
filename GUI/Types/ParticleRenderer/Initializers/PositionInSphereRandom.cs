using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class PositionInSphereRandom : IParticleInitializer
    {
        public float RadiusMin { get; }
        public float RadiusMax { get; }

        private readonly Random random;

        public PositionInSphereRandom(IKeyValueCollection keyValues)
        {
            random = new Random();

            RadiusMin = keyValues.GetIntegerProperty("m_fRadiusMin");
            RadiusMax = keyValues.GetIntegerProperty("m_fRadiusMax");
        }

        public Particle Initialize(Particle particle)
        {
            var radius = (float)(random.NextDouble() * (RadiusMax - RadiusMin)) + RadiusMin;

            var direction = new Vector3(
                (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f,
                (float)random.NextDouble() - 0.5f);

            direction /= direction.Length(); // Normalize

            var position = direction * radius;

            particle.Position = position;
            return particle;
        }
    }
}

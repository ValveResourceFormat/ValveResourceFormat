using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class CreateWithinSphere : IParticleInitializer
    {
        private readonly float radiusMin = 0f;
        private readonly float radiusMax = 0f;
        private readonly float speedMin = 0f;
        private readonly float speedMax = 0f;

        private readonly Random random;

        public CreateWithinSphere(IKeyValueCollection keyValues)
        {
            random = new Random();

            if (keyValues.ContainsKey("m_fRadiusMin"))
            {
                radiusMin = keyValues.GetFloatProperty("m_fRadiusMin");
            }

            if (keyValues.ContainsKey("m_fRadiusMax"))
            {
                radiusMax = keyValues.GetFloatProperty("m_fRadiusMax");
            }

            if (keyValues.ContainsKey("m_fSpeedMin"))
            {
                speedMin = keyValues.GetFloatProperty("m_fSpeedMin");
            }

            if (keyValues.ContainsKey("m_fSpeedMax"))
            {
                speedMax = keyValues.GetFloatProperty("m_fSpeedMax");
            }
        }

        public Particle Initialize(Particle particle)
        {
            var randomVector = new Vector3(
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1);

            // Normalize
            var direction = randomVector / randomVector.Length();

            var distance = radiusMin + ((float)random.NextDouble() * (radiusMax - radiusMin));
            var speed = speedMin + ((float)random.NextDouble() * (speedMax - speedMin));

            particle.Position = direction * distance;
            particle.Velocity = direction * speed;

            return particle;
        }
    }
}

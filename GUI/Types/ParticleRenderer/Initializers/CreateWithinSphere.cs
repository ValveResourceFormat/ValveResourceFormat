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
        private readonly Vector3 localCoordinateSystemSpeedMin;
        private readonly Vector3 localCoordinateSystemSpeedMax;

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

            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMin"))
            {
                var vectorValues = keyValues.GetArray<double>("m_LocalCoordinateSystemSpeedMin");
                localCoordinateSystemSpeedMin = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }

            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMax"))
            {
                var vectorValues = keyValues.GetArray<double>("m_LocalCoordinateSystemSpeedMax");
                localCoordinateSystemSpeedMax = new Vector3((float)vectorValues[0], (float)vectorValues[1], (float)vectorValues[2]);
            }
        }

        public Particle Initialize(Particle particle, ParticleSystemRenderState particleSystemRenderState)
        {
            var randomVector = new Vector3(
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1);

            // Normalize
            var direction = randomVector / randomVector.Length();

            var distance = radiusMin + ((float)random.NextDouble() * (radiusMax - radiusMin));
            var speed = speedMin + ((float)random.NextDouble() * (speedMax - speedMin));

            var localCoordinateSystemSpeed = localCoordinateSystemSpeedMin
                + ((float)random.NextDouble() * (localCoordinateSystemSpeedMax - localCoordinateSystemSpeedMin));

            particle.Position = direction * distance;
            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}

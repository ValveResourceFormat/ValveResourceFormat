using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class CreateWithinSphere : IParticleInitializer
    {
        private readonly INumberProvider radiusMin = new LiteralNumberProvider(0);
        private readonly INumberProvider radiusMax = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0);
        private readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        private readonly Random random;

        public CreateWithinSphere(IKeyValueCollection keyValues)
        {
            random = new Random();

            if (keyValues.ContainsKey("m_fRadiusMin"))
            {
                radiusMin = keyValues.GetNumberProvider("m_fRadiusMin");
            }

            if (keyValues.ContainsKey("m_fRadiusMax"))
            {
                radiusMax = keyValues.GetNumberProvider("m_fRadiusMax");
            }

            if (keyValues.ContainsKey("m_fSpeedMin"))
            {
                speedMin = keyValues.GetNumberProvider("m_fSpeedMin");
            }

            if (keyValues.ContainsKey("m_fSpeedMax"))
            {
                speedMax = keyValues.GetNumberProvider("m_fSpeedMax");
            }

            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMin"))
            {
                localCoordinateSystemSpeedMin = keyValues.GetVectorProvider("m_LocalCoordinateSystemSpeedMin");
            }

            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMax"))
            {
                localCoordinateSystemSpeedMax = keyValues.GetVectorProvider("m_LocalCoordinateSystemSpeedMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var randomVector = new Vector3(
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1,
                ((float)random.NextDouble() * 2) - 1);

            // Normalize
            var direction = randomVector / randomVector.Length();

            var distance = (float)(radiusMin.NextNumber() + (random.NextDouble() * (radiusMax.NextNumber() - radiusMin.NextNumber())));
            var speed = (float)(speedMin.NextNumber() + (random.NextDouble() * (speedMax.NextNumber() - speedMin.NextNumber())));

            var localSystemSpeedMin = localCoordinateSystemSpeedMin.NextVector();
            var localSystemSpeedMax = localCoordinateSystemSpeedMax.NextVector();

            var localCoordinateSystemSpeed = localSystemSpeedMin
                + ((float)random.NextDouble() * (localSystemSpeedMax - localSystemSpeedMin));

            particle.Position += direction * distance;
            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}

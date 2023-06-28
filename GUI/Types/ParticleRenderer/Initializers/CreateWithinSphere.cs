using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class CreateWithinSphere : IParticleInitializer
    {
        private readonly INumberProvider radiusMin = new LiteralNumberProvider(0);
        private readonly INumberProvider radiusMax = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0);
        private readonly IVectorProvider localCoordinateSystemSpeedMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider localCoordinateSystemSpeedMax = new LiteralVectorProvider(Vector3.Zero);

        public CreateWithinSphere(IKeyValueCollection keyValues)
        {
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
            var randomVector = MathUtils.RandomBetweenPerComponent(new Vector3(-1), new Vector3(1));

            // Normalize
            var direction = Vector3.Normalize(randomVector);

            var distance = MathUtils.RandomBetween(
                radiusMin.NextNumber(particle, particleSystemState),
                radiusMax.NextNumber(particle, particleSystemState));

            var speed = MathUtils.RandomBetween(
                speedMin.NextNumber(particle, particleSystemState),
                speedMax.NextNumber(particle, particleSystemState));

            var localCoordinateSystemSpeed = MathUtils.RandomBetweenPerComponent(
                localCoordinateSystemSpeedMin.NextVector(particle, particleSystemState),
                localCoordinateSystemSpeedMax.NextVector(particle, particleSystemState));

            particle.InitialPosition += direction * distance;
            particle.Position = particle.InitialPosition;

            particle.Velocity = (direction * speed) + localCoordinateSystemSpeed;

            return particle;
        }
    }
}

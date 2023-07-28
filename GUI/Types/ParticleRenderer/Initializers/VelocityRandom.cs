using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class VelocityRandom : IParticleInitializer
    {
        private readonly IVectorProvider vectorMin = new LiteralVectorProvider(Vector3.Zero);
        private readonly IVectorProvider vectorMax = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0.1f);

        public VelocityRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMin"))
            {
                vectorMin = keyValues.GetVectorProvider("m_LocalCoordinateSystemSpeedMin");
            }

            if (keyValues.ContainsKey("m_LocalCoordinateSystemSpeedMax"))
            {
                vectorMax = keyValues.GetVectorProvider("m_LocalCoordinateSystemSpeedMax");
            }

            if (keyValues.ContainsKey("m_fSpeedMin"))
            {
                speedMin = keyValues.GetNumberProvider("m_fSpeedMin");
            }

            if (keyValues.ContainsKey("m_fSpeedMax"))
            {
                speedMax = keyValues.GetNumberProvider("m_fSpeedMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            // A bit unclear what the speed is here, but I do know that going under 1.0 does nothing different than 1.0
            var speedmin = speedMin.NextNumber(ref particle, particleSystemState);
            var speedmax = speedMax.NextNumber(ref particle, particleSystemState);

            var speed = Math.Max(1.0f, MathUtils.RandomBetween(speedmin, speedmax));

            var vecMin = vectorMin.NextVector(ref particle, particleSystemState);
            var vecMax = vectorMax.NextVector(ref particle, particleSystemState);

            var velocity = MathUtils.RandomBetweenPerComponent(vecMin, vecMax);

            particle.Velocity = velocity * speed;

            return particle;
        }
    }
}

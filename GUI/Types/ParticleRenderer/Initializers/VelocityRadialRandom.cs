using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class VelocityRadialRandom : IParticleInitializer
    {
        // unsure if this is actually a vector provider
        private readonly IVectorProvider vectorScale = new LiteralVectorProvider(Vector3.Zero);
        private readonly INumberProvider speedMin = new LiteralNumberProvider(0.1f);
        private readonly INumberProvider speedMax = new LiteralNumberProvider(0.1f);

        private readonly int controlPoint;
        private readonly bool ignoreDelta;

        public VelocityRadialRandom(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_vecLocalCoordinateSystemSpeedScale"))
            {
                vectorScale = keyValues.GetVectorProvider("m_vecLocalCoordinateSystemSpeedScale");
            }

            if (keyValues.ContainsKey("m_fSpeedMin"))
            {
                speedMin = keyValues.GetNumberProvider("m_fSpeedMin");
            }

            if (keyValues.ContainsKey("m_fSpeedMax"))
            {
                speedMax = keyValues.GetNumberProvider("m_fSpeedMax");
            }

            if (keyValues.ContainsKey("m_bIgnoreDelta"))
            {
                ignoreDelta = keyValues.GetProperty<bool>("m_bIgnoreDelta");
            }

            if (keyValues.ContainsKey("m_nControlPointNumber"))
            {
                controlPoint = keyValues.GetInt32Property("m_nControlPointNumber");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var speedmin = speedMin.NextNumber(particle, particleSystemState);
            var speedmax = speedMax.NextNumber(particle, particleSystemState);

            var speed = Math.Max(1.0f, MathUtils.RandomBetween(speedmin, speedmax));

            if (ignoreDelta)
            {
                // We can't currently access the delta time in initializers, so we have a template here.
                var deltaTimeFake = 1.0f;
                speed /= deltaTimeFake;
            }

            var scale = vectorScale.NextVector(particle, particleSystemState);

            var direction = Vector3.Normalize(particle.Position - particleSystemState.GetControlPoint(controlPoint).Position);

            particle.Velocity = direction * speed * scale;

            return particle;
        }
    }
}

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

        public VelocityRadialRandom(ParticleDefinitionParser parse)
        {
            vectorScale = parse.VectorProvider("m_vecLocalCoordinateSystemSpeedScale", vectorScale);

            speedMin = parse.NumberProvider("m_fSpeedMin", speedMin);

            speedMax = parse.NumberProvider("m_fSpeedMax", speedMax);

            ignoreDelta = parse.Boolean("m_bIgnoreDelta", ignoreDelta);

            controlPoint = parse.Int32("m_nControlPointNumber", controlPoint);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var speedmin = speedMin.NextNumber(ref particle, particleSystemState);
            var speedmax = speedMax.NextNumber(ref particle, particleSystemState);

            var speed = Math.Max(1.0f, MathUtils.RandomBetween(speedmin, speedmax));

            if (ignoreDelta)
            {
                // We can't currently access the delta time in initializers, so we have a template here.
                var deltaTimeFake = 1.0f;
                speed /= deltaTimeFake;
            }

            var scale = vectorScale.NextVector(ref particle, particleSystemState);

            var direction = Vector3.Normalize(particle.Position - particleSystemState.GetControlPoint(controlPoint).Position);

            particle.Velocity = direction * speed * scale;

            return particle;
        }
    }
}

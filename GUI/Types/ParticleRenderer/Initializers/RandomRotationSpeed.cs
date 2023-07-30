using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRotationSpeed : IParticleInitializer
    {
        private readonly ParticleField FieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection = true;
        private readonly float degrees;
        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        public RandomRotationSpeed(ParticleDefinitionParser parse)
        {
            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);
            randomlyFlipDirection = parse.Boolean("m_bRandomlyFlipDirection", randomlyFlipDirection);
            degrees = parse.Float("m_flDegrees", degrees);
            degreesMin = parse.Float("m_flDegreesMin", degreesMin);
            degreesMax = parse.Float("m_flDegreesMax", degreesMax);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = MathUtils.ToRadians(degrees + ParticleCollection.RandomBetween(particle.ParticleID, degreesMin, degreesMax));

            if (randomlyFlipDirection && Random.Shared.NextSingle() > 0.5f)
            {
                value *= -1;
            }

            if (FieldOutput == ParticleField.Yaw)
            {
                particle.RotationSpeed = new Vector3(value, 0, 0);
            }
            else if (FieldOutput == ParticleField.Roll)
            {
                particle.RotationSpeed = new Vector3(0, 0, value);
            }

            return particle;
        }
    }
}

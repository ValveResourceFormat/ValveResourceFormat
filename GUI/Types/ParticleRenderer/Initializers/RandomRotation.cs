using System;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRotation : IParticleInitializer
    {
        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        private readonly float degreesOffset;
        private readonly float randomExponent = 1.0f;
        private readonly ParticleField FieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection;

        public RandomRotation(ParticleDefinitionParser parse)
        {
            degreesMin = parse.Float("m_flDegreesMin", degreesMin);

            degreesMax = parse.Float("m_flDegreesMax", degreesMax);

            degreesOffset = parse.Float("m_flDegrees", degreesOffset);

            FieldOutput = parse.ParticleField("m_nFieldOutput", FieldOutput);

            randomlyFlipDirection = parse.Boolean("m_bRandomlyFlipDirection", randomlyFlipDirection);

            randomExponent = parse.Float("m_flRotationRandExponent", randomExponent);
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var degrees = degreesOffset + MathUtils.RandomWithExponentBetween(randomExponent, degreesMin, degreesMax);
            if (randomlyFlipDirection && Random.Shared.NextSingle() > 0.5f)
            {
                degrees *= -1;
            }

            particle.SetScalar(FieldOutput, MathUtils.ToRadians(degrees));

            return particle;
        }
    }
}

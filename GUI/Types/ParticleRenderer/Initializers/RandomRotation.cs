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
        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection;

        public RandomRotation(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_flDegreesMin"))
            {
                degreesMin = keyValues.GetFloatProperty("m_flDegreesMin");
            }

            if (keyValues.ContainsKey("m_flDegreesMax"))
            {
                degreesMax = keyValues.GetFloatProperty("m_flDegreesMax");
            }

            if (keyValues.ContainsKey("m_flDegrees"))
            {
                degreesOffset = keyValues.GetFloatProperty("m_flDegrees");
            }

            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_bRandomlyFlipDirection"))
            {
                randomlyFlipDirection = keyValues.GetProperty<bool>("m_bRandomlyFlipDirection");
            }

            if (keyValues.ContainsKey("m_flRotationRandExponent"))
            {
                randomExponent = keyValues.GetFloatProperty("m_flRotationRandExponent");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var degrees = degreesOffset + MathUtils.RandomWithExponentBetween(randomExponent, degreesMin, degreesMax);
            if (randomlyFlipDirection && Random.Shared.NextSingle() > 0.5f)
            {
                degrees *= -1;
            }

            particle.SetScalar(fieldOutput, MathUtils.ToRadians(degrees));

            return particle;
        }
    }
}

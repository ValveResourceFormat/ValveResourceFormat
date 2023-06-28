using System;
using System.Numerics;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    class RandomRotationSpeed : IParticleInitializer
    {
        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection = true;
        private readonly float degrees;
        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        public RandomRotationSpeed(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = keyValues.GetParticleField("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_bRandomlyFlipDirection"))
            {
                randomlyFlipDirection = keyValues.GetProperty<bool>("m_bRandomlyFlipDirection");
            }

            if (keyValues.ContainsKey("m_flDegrees"))
            {
                degrees = keyValues.GetFloatProperty("m_flDegrees");
            }

            if (keyValues.ContainsKey("m_flDegreesMin"))
            {
                degreesMin = keyValues.GetFloatProperty("m_flDegreesMin");
            }

            if (keyValues.ContainsKey("m_flDegreesMax"))
            {
                degreesMax = keyValues.GetFloatProperty("m_flDegreesMax");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var value = MathUtils.ToRadians(degrees + MathUtils.RandomBetween(degreesMin, degreesMax));

            if (randomlyFlipDirection && Random.Shared.NextSingle() > 0.5f)
            {
                value *= -1;
            }

            if (fieldOutput == ParticleField.Yaw)
            {
                particle.RotationSpeed = new Vector3(value, 0, 0);
            }
            else if (fieldOutput == ParticleField.Roll)
            {
                particle.RotationSpeed = new Vector3(0, 0, value);
            }

            return particle;
        }
    }
}

using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomRotationSpeed : IParticleInitializer
    {
        private const float PiOver180 = (float)Math.PI / 180f;

        private readonly ParticleField fieldOutput = ParticleField.Roll;
        private readonly bool randomlyFlipDirection = true;
        private readonly float degrees = 0f;
        private readonly float degreesMin = 0f;
        private readonly float degreesMax = 360f;

        private readonly Random random = new Random();

        public RandomRotationSpeed(IKeyValueCollection keyValues)
        {
            if (keyValues.ContainsKey("m_nFieldOutput"))
            {
                fieldOutput = (ParticleField)keyValues.GetIntegerProperty("m_nFieldOutput");
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
            var value = PiOver180 * (degrees + degreesMin + ((float)random.NextDouble() * (degreesMax - degreesMin)));

            if (randomlyFlipDirection && random.NextDouble() > 0.5)
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

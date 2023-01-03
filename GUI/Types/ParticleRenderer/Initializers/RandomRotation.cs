using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer.Initializers
{
    public class RandomRotation : IParticleInitializer
    {
        private const float PiOver180 = (float)Math.PI / 180f;

        private readonly float degreesMin;
        private readonly float degreesMax = 360f;
        private readonly float degreesOffset;
        private readonly long fieldOutput = 4;
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
                fieldOutput = keyValues.GetIntegerProperty("m_nFieldOutput");
            }

            if (keyValues.ContainsKey("m_bRandomlyFlipDirection"))
            {
                randomlyFlipDirection = keyValues.GetProperty<bool>("m_bRandomlyFlipDirection");
            }
        }

        public Particle Initialize(ref Particle particle, ParticleSystemRenderState particleSystemState)
        {
            var degrees = degreesOffset + degreesMin + ((float)Random.Shared.NextDouble() * (degreesMax - degreesMin));
            if (randomlyFlipDirection && Random.Shared.NextDouble() > 0.5)
            {
                degrees *= -1;
            }

            if (fieldOutput == 4)
            {
                // Roll
                particle.Rotation = new Vector3(particle.Rotation.X, particle.Rotation.Y, degrees * PiOver180);
            }
            else if (fieldOutput == 12)
            {
                // Yaw
                particle.Rotation = new Vector3(particle.Rotation.X, degrees * PiOver180, particle.Rotation.Z);
            }

            return particle;
        }
    }
}

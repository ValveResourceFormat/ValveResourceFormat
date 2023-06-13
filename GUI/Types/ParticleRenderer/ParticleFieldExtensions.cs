using System.Numerics;

namespace GUI.Types.ParticleRenderer
{
    public enum ParticleField
    {
        Position = 0,
        PositionPrevious = 2,
        Radius = 3,
        Roll = 4,
        Alpha = 7,
        Yaw = 12,
        AlphaAlternate = 16,
    }

    static class ParticleFieldExtensions
    {
        public static float GetScalar(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Alpha => particle.Alpha,
                ParticleField.AlphaAlternate => particle.AlphaAlternate,
                ParticleField.Radius => particle.Radius,
                _ => 0f,
            };
        }

        public static Vector3 GetVector(this Particle particle, ParticleField field)
        {
            return field switch
            {
                ParticleField.Position => particle.Position,
                ParticleField.PositionPrevious => particle.PositionPrevious,
                _ => Vector3.Zero,
            };
        }
    }
}

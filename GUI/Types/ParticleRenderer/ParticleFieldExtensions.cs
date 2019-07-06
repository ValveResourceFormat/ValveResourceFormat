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

    public static class ParticleFieldExtensions
    {
        public static float GetScalar(this Particle particle, ParticleField field)
        {
            switch (field)
            {
                case ParticleField.Alpha: return particle.Alpha;
                case ParticleField.AlphaAlternate: return particle.AlphaAlternate;
                case ParticleField.Radius: return particle.Radius;
            }

            return 0f;
        }

        public static Vector3 GetVector(this Particle particle, ParticleField field)
        {
            switch (field)
            {
                case ParticleField.Position: return particle.Position;
                case ParticleField.PositionPrevious: return particle.PositionPrevious;
            }

            return Vector3.Zero;
        }
    }
}

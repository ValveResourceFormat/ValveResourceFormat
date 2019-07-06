using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public class Particle
    {
        public long ParticleCount { get; set; }

        // Base properties
        public float ConstantAlpha { get; set; } = 1f;
        public Vector3 ConstantColor { get; set; } = Vector3.One;
        public float ConstantLifetime { get; set; } = 1f;
        public float ConstantRadius { get; set; } = 5f;

        // Variable fields
        public float Alpha { get; set; } = 1;
        public float AlphaAlternate { get; set; } = 1;

        public Vector3 Color { get; set; }

        public float Lifetime { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 PositionPrevious { get; set; }

        public float Radius { get; set; }

        public float TrailLength { get; set; } = 1f;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles.
        /// </summary>
        public Vector3 Rotation { get; set; }

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed { get; set; }

        public Vector3 Velocity { get; set; }

        public Particle()
        {
            Init();
        }

        public Particle(IKeyValueCollection baseProperties)
        {
            if (baseProperties.ContainsKey("m_ConstantColor"))
            {
                var vectorValues = baseProperties.GetArray<long>("m_ConstantColor");
                ConstantColor = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
            }

            if (baseProperties.ContainsKey("m_flConstantRadius"))
            {
                ConstantRadius = baseProperties.GetFloatProperty("m_flConstantRadius");
            }

            if (baseProperties.ContainsKey("m_flConstantLifespan"))
            {
                ConstantLifetime = baseProperties.GetFloatProperty("m_flConstantLifespan");
            }

            Init();
        }

        private void Init()
        {
            Color = ConstantColor;
            Lifetime = ConstantLifetime;
            Radius = ConstantRadius;
        }

        public OpenTK.Matrix4 GetTransformationMatrix()
        {
            var scaleMatrix = OpenTK.Matrix4.CreateScale(Radius);
            var translationMatrix = OpenTK.Matrix4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return scaleMatrix * translationMatrix;
        }

        public OpenTK.Matrix4 GetRotationMatrix()
        {
            var rotationMatrix = OpenTK.Matrix4.CreateRotationZ(Rotation.Z) * OpenTK.Matrix4.CreateRotationY(Rotation.Y);
            return rotationMatrix;
        }
    }
}

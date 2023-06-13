using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    struct Particle
    {
        public int ParticleCount { get; set; }

        // Base properties
        public float ConstantAlpha { get; set; }
        public Vector3 ConstantColor { get; set; }
        public float ConstantLifetime { get; set; }
        public float ConstantRadius { get; set; }

        // Variable fields
        public float Alpha { get; set; }
        public float AlphaAlternate { get; set; }

        public Vector3 Color { get; set; }

        public float Lifetime { get; set; }

        public Vector3 Position { get; set; }

        public Vector3 PositionPrevious { get; set; }

        public float Radius { get; set; }

        public float TrailLength { get; set; }

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles.
        /// </summary>
        public Vector3 Rotation { get; set; }

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed { get; set; }

        public int Sequence { get; set; }

        public Vector3 Velocity { get; set; }

        public Particle()
        {
            ParticleCount = 0;
            Alpha = 1.0f;
            AlphaAlternate = 1.0f;
            Color = Vector3.One;
            Lifetime = 1f;
            Position = Vector3.Zero;
            PositionPrevious = Vector3.Zero;
            Radius = 5.0f;
            Rotation = Vector3.Zero;
            RotationSpeed = Vector3.Zero;
            Velocity = Vector3.Zero;
            TrailLength = 1;
            Sequence = 0;

            ConstantAlpha = Alpha;
            ConstantColor = Color;
            ConstantLifetime = Lifetime;
            ConstantRadius = Radius;
        }

        public Particle(IKeyValueCollection baseProperties) : this()
        {
            if (baseProperties.ContainsKey("m_ConstantColor"))
            {
                var vectorValues = baseProperties.GetIntegerArray("m_ConstantColor");
                ConstantColor = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
                Color = ConstantColor;
            }

            if (baseProperties.ContainsKey("m_flConstantRadius"))
            {
                ConstantRadius = baseProperties.GetFloatProperty("m_flConstantRadius");
                Radius = ConstantRadius;
            }

            if (baseProperties.ContainsKey("m_flConstantLifespan"))
            {
                ConstantLifetime = baseProperties.GetFloatProperty("m_flConstantLifespan");
                Lifetime = ConstantLifetime;
            }
        }

        public Matrix4x4 GetTransformationMatrix()
        {
            var scaleMatrix = Matrix4x4.CreateScale(Radius);
            var translationMatrix = Matrix4x4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return Matrix4x4.Multiply(scaleMatrix, translationMatrix);
        }

        public Matrix4x4 GetRotationMatrix()
        {
            var rotationMatrix = Matrix4x4.Multiply(Matrix4x4.CreateRotationZ(Rotation.Z), Matrix4x4.CreateRotationY(Rotation.Y));
            return rotationMatrix;
        }
    }
}

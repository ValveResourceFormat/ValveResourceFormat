using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    public struct Particle
    {
        public int ParticleCount;

        // Base properties
        public float ConstantAlpha;
        public Vector3 ConstantColor;
        public float ConstantLifetime;
        public float ConstantRadius;

        // Variable fields
        public float Alpha;
        public float AlphaAlternate;

        public Vector3 Color;

        public float Lifetime;

        public Vector3 Position;

        public Vector3 PositionPrevious;

        public float Radius;

        public float TrailLength;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles.
        /// </summary>
        public Vector3 Rotation;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed;

        public int Sequence;

        public Vector3 Velocity;

        public Particle(IKeyValueCollection baseProperties)
        {
            ParticleCount = 0;
            Alpha = 1;
            AlphaAlternate = 1;
            Position = Vector3.Zero;
            PositionPrevious = Vector3.Zero;
            Rotation = Vector3.Zero;
            RotationSpeed = Vector3.Zero;
            Velocity = Vector3.Zero;
            ConstantRadius = 5.0f;
            ConstantAlpha = 1.0f;
            ConstantColor = Vector3.One;
            ConstantLifetime = 1;
            TrailLength = 1;
            Sequence = 0;

            if (baseProperties.ContainsKey("m_ConstantColor"))
            {
                var vectorValues = baseProperties.GetIntegerArray("m_ConstantColor");
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

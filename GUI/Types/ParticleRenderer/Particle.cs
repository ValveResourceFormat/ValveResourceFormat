using System;
using System.Numerics;
using ValveResourceFormat.Serialization;

namespace GUI.Types.ParticleRenderer
{
    struct Particle
    {
        public static Particle @default;
        public static ref Particle Default => ref @default;

        public int BehaviorVersion { get; set; }
        public int ParticleCount { get; set; } // starts at 0

        // Initial properties (set by initializers, and read whenever we want the "initial value" instead of the current one)
        public Vector3 InitialPosition { get; set; } = Vector3.Zero;
        public float InitialAlpha { get; set; } = 1.0f;
        public Vector3 InitialColor { get; set; } = Vector3.One;
        public float InitialLifetime { get; set; } = 1.0f;
        public float InitialRadius { get; set; } = 1.0f;
        public Vector3 InitialRotation { get; set; } = Vector3.Zero;
        public Vector3 InitialRotationSpeed { get; set; } = Vector3.Zero;
        public int InitialSequence { get; set; } = 0;
        public int InitialSequence2 { get; set; } = 0;


        // Varying properties (read from initializers but then change afterwards)
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 PositionPrevious { get; set; } = Vector3.Zero; // Used for velocity computation
        public float Age { get; set; } = 0f;
        public float Lifetime { get; set; }

        public float Alpha { get; set; } = 1.0f;
        public float AlphaAlternate { get; set; } = 1.0f;

        public Vector3 Color { get; set; } = Vector3.One; // ??
        public float Radius { get; set; } = 1.0f;

        public float TrailLength { get; set; } = 0f;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles.
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed { get; set; } = Vector3.Zero;
        public Vector3 Velocity { get; set; } = Vector3.Zero;
        public readonly float NormalizedAge => Age / Math.Max(0.0001f, Lifetime); //Old version: 1 - (Lifetime / ConstantLifetime);
        public float Speed
        {
            get => Velocity.Length();
            set => Velocity = Vector3.Normalize(Velocity) * value;
        }
        public int Sequence { get; set; } = 0;

        // Varying properties that we don't really support but are here in case they're used across operators
        public int Sequence2 { get; set; } = 0;

        public float AlphaWindowThreshold { get; set; } = 0f;
        public float ScratchFloat0 { get; set; } = 0f;
        public float ScratchFloat1 { get; set; } = 0f;
        public float ScratchFloat2 { get; set; } = 0f;
        public Vector3 ScratchVector { get; set; } = Vector3.Zero;
        public Vector3 ScratchVector2 { get; set; } = Vector3.Zero;
        public float CreationTime { get; set; } // todo

        public bool MarkedAsKilled { get; set; } = false;
        public Particle(IKeyValueCollection baseProperties)
        {
            Age = 0;
            Alpha = 1.0f;
            AlphaAlternate = 1.0f;
            Rotation = Vector3.Zero;
            RotationSpeed = Vector3.Zero;
            Velocity = Vector3.Zero;
            TrailLength = 1;
            Sequence = 0;
            Sequence2 = 0;
            CreationTime = 0;
            AlphaWindowThreshold = 0;
            ScratchFloat0 = 0.0f;
            ScratchFloat1 = 0.0f;
            ScratchFloat2 = 0.0f;

            InitialSequence = 0;
            InitialSequence2 = 0;

            // Yeah, naming these "InitialColor" and stuff will make this more confusing, but it will make everything else much more intuitive.
            if (baseProperties.ContainsKey("m_ConstantColor"))
            {
                var vectorValues = baseProperties.GetIntegerArray("m_ConstantColor");
                InitialColor = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
                InitialAlpha = vectorValues[3] / 255f; // presumably
            }

            if (baseProperties.ContainsKey("m_flConstantRadius"))
            {
                InitialRadius = baseProperties.GetFloatProperty("m_flConstantRadius");
            }

            if (baseProperties.ContainsKey("m_flConstantLifespan"))
            {
                InitialLifetime = baseProperties.GetFloatProperty("m_flConstantLifespan");
            }

            if (baseProperties.ContainsKey("m_flConstantRotation"))
            {
                InitialRotation = new Vector3(0.0f, 0.0f, baseProperties.GetFloatProperty("m_flConstantRotation"));
            }

            if (baseProperties.ContainsKey("m_flConstantRotationSpeed"))
            {
                InitialRotationSpeed = new Vector3(0.0f, 0.0f, baseProperties.GetFloatProperty("m_flConstantRotationSpeed"));
            }

            if (baseProperties.ContainsKey("m_nConstantSequenceNumber"))
            {
                InitialSequence = baseProperties.GetInt32Property("m_nConstantSequenceNumber");
            }

            if (baseProperties.ContainsKey("m_nConstantSequenceNumber1"))
            {
                InitialSequence2 = baseProperties.GetInt32Property("m_nConstantSequenceNumber1");
            }

            Color = InitialColor;
            Lifetime = InitialLifetime;
            Radius = InitialRadius;
            Rotation = InitialRotation;
            RotationSpeed = InitialRotationSpeed;
            Sequence = InitialSequence;
            Sequence2 = InitialSequence2;

            BehaviorVersion = 0;
            if (baseProperties.ContainsKey("m_nBehaviorVersion"))
            {
                BehaviorVersion = baseProperties.GetInt32Property("m_nBehaviorVersion");
            }
        }

        public Matrix4x4 GetTransformationMatrix(float radiusScale = 1f)
        {
            var scaleMatrix = Matrix4x4.CreateScale(Radius * radiusScale);
            var translationMatrix = Matrix4x4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return Matrix4x4.Multiply(scaleMatrix, translationMatrix);
        }

        public Matrix4x4 GetRotationMatrix()
        {
            var rotationMatrix = Matrix4x4.Multiply(Matrix4x4.CreateRotationZ(Rotation.Z), Matrix4x4.CreateRotationY(Rotation.Y));
            return rotationMatrix;
        }

        // Mark particle for removal
        public void Kill()
        {
            MarkedAsKilled = true;
        }
    }
}

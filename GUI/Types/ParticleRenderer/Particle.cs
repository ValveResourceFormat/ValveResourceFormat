using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.ParticleRenderer
{
    struct Particle
    {
        public static Particle @default;
        public static ref Particle Default => ref @default;

        public int ParticleID { get; set; } // starts at 0

        // Varying properties (read from initializers but then change afterwards)
        public Vector3 Position { get; set; } = Vector3.Zero;
        public Vector3 PositionPrevious { get; set; } = Vector3.Zero; // Used for velocity computation
        public float Age { get; set; } = 0f;
        public float Lifetime { get; set; } = 1f;

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
            readonly get => Velocity.Length();
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
        public int Index = 0;

        public Particle(ParticleDefinitionParser parse)
        {
            if (parse.Data.ContainsKey("m_ConstantColor"))
            {
                var vectorValues = parse.Data.GetIntegerArray("m_ConstantColor");
                Color = new Vector3(vectorValues[0], vectorValues[1], vectorValues[2]) / 255f;
                Alpha = vectorValues[3] / 255f; // presumably
            }

            Radius = parse.Float("m_flConstantRadius", Radius);
            Lifetime = parse.Float("m_flConstantLifespan", Lifetime);
            Rotation = Rotation with { Z = parse.Float("m_flConstantRotation", Rotation.Z) };
            Rotation = Rotation with { Z = parse.Float("m_flConstantRotationSpeed", Rotation.Z) };
            Sequence = parse.Int32("m_nConstantSequenceNumber", Sequence);
            Sequence = parse.Int32("m_nConstantSequenceNumber1", Sequence);
        }

        public readonly Matrix4x4 GetTransformationMatrix(float radiusScale = 1f)
        {
            var scaleMatrix = Matrix4x4.CreateScale(Radius * radiusScale);
            var translationMatrix = Matrix4x4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return Matrix4x4.Multiply(scaleMatrix, translationMatrix);
        }

        public readonly Matrix4x4 GetRotationMatrix()
        {
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z);
            return rotationMatrix;
        }

        // Mark particle for removal
        public void Kill()
        {
            MarkedAsKilled = true;
        }
    }
}

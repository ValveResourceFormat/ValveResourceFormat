using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Renderer.Particles
{
    /// <summary>
    /// Represents a single particle instance with all its runtime properties.
    /// </summary>
    struct Particle
    {
        /// <summary>A default particle instance used as a placeholder when per-particle context is unavailable.</summary>
        public static Particle @default;
        /// <summary>Gets a reference to the shared default particle instance.</summary>
        public static ref Particle Default => ref @default;

        /// <summary>Gets or sets the unique particle ID, starting at 0 for each emission.</summary>
        public int ParticleID { get; set; } // starts at 0

        // Varying properties (read from initializers but then change afterwards)
        /// <summary>Gets or sets the current world-space position of the particle.</summary>
        public Vector3 Position { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets the world-space position from the previous frame, used for velocity computation.</summary>
        public Vector3 PositionPrevious { get; set; } = Vector3.Zero; // Used for velocity computation
        /// <summary>Gets or sets the current age of the particle in seconds.</summary>
        public float Age { get; set; } = 0f;
        /// <summary>Gets or sets the total lifetime of the particle in seconds.</summary>
        public float Lifetime { get; set; } = 1f;

        /// <summary>Gets or sets the alpha (opacity) of the particle, in the range [0, 1].</summary>
        public float Alpha { get; set; } = 1.0f;
        /// <summary>Gets or sets an alternate alpha value used by some operators and renderers.</summary>
        public float AlphaAlternate { get; set; } = 1.0f;

        /// <summary>Gets or sets the RGB color of the particle, with each component in the range [0, 1].</summary>
        public Vector3 Color { get; set; } = Vector3.One; // ??
        /// <summary>Gets or sets the radius of the particle.</summary>
        public float Radius { get; set; } = 1.0f;

        /// <summary>Gets or sets the trail length multiplier for trail-based renderers.</summary>
        public float TrailLength { get; set; } = 0f;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles.
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets the current velocity of the particle.</summary>
        public Vector3 Velocity { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets the normalized direction vector derived from the particle's yaw/pitch rotation.
        /// </summary>
        public Vector3 Normal
        {
            readonly get => Vector3.Transform(new Vector3(0, 0, 1), GetRotationMatrix());
            set
            {
                var normal = Vector3.Normalize(value);

                if (normal == Vector3.Zero)
                {
                    return;
                }

                var yaw = MathF.Atan2(normal.X, normal.Z);
                var pitch = MathF.Asin(Math.Clamp(normal.Y, -1f, 1f));
                Rotation = new Vector3(yaw, pitch, Rotation.Z);
            }
        }

        /// <summary>Gets the particle's age as a fraction of its lifetime. May exceed 1 if the particle outlives its lifetime.</summary>
        public readonly float NormalizedAge => Age / Math.Max(0.0001f, Lifetime); //Old version: 1 - (Lifetime / ConstantLifetime);
        /// <summary>Gets or sets the scalar speed of the particle, adjusting velocity direction when set.</summary>
        public float Speed
        {
            readonly get => Velocity.Length();
            set => Velocity = Vector3.Normalize(Velocity) * value;
        }
        /// <summary>Gets or sets the sprite sheet sequence number.</summary>
        public int Sequence { get; set; } = 0;

        /// <summary>Gets or sets the manually selected animation frame index.</summary>
        public int ManualAnimationFrame { get; set; } = 0;

        // Varying properties that we don't really support but are here in case they're used across operators
        /// <summary>Gets or sets a secondary sprite sheet sequence number.</summary>
        public int Sequence2 { get; set; } = 0;

        /// <summary>Gets or sets the index of the particle's parent particle in a parent system.</summary>
        public int ParentParticleIndex { get; set; } = -1;

        /// <summary>Gets or sets the alpha window threshold scratch value.</summary>
        public float AlphaWindowThreshold { get; set; } = 0f;
        /// <summary>Gets or sets the first general-purpose scratch float.</summary>
        public float ScratchFloat0 { get; set; } = 0f;
        /// <summary>Gets or sets the second general-purpose scratch float.</summary>
        public float ScratchFloat1 { get; set; } = 0f;
        /// <summary>Gets or sets the third general-purpose scratch float.</summary>
        public float ScratchFloat2 { get; set; } = 0f;
        /// <summary>Gets or sets a general-purpose scratch vector.</summary>
        public Vector3 ScratchVector { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets a second general-purpose scratch vector.</summary>
        public Vector3 ScratchVector2 { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets the system time at which this particle was created.</summary>
        public float CreationTime { get; set; } // todo

        /// <summary>Gets or sets a value indicating whether this particle has been marked for removal.</summary>
        public bool MarkedAsKilled { get; set; } = false;
        /// <summary>The index of this particle within its collection's arrays.</summary>
        public int Index = 0;

        /// <summary>
        /// Initializes a new <see cref="Particle"/> using constant attributes from a particle system definition.
        /// </summary>
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

        /// <summary>
        /// Returns a combined scale-translation matrix for this particle.
        /// </summary>
        /// <param name="radiusScale">Optional additional scale factor applied to the radius.</param>
        public readonly Matrix4x4 GetTransformationMatrix(float radiusScale = 1f)
        {
            var scaleMatrix = Matrix4x4.CreateScale(Radius * radiusScale);
            var translationMatrix = Matrix4x4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return Matrix4x4.Multiply(scaleMatrix, translationMatrix);
        }

        /// <summary>
        /// Returns a rotation matrix derived from the particle's Euler angles.
        /// </summary>
        public readonly Matrix4x4 GetRotationMatrix()
        {
            var rotationMatrix = Matrix4x4.CreateFromYawPitchRoll(Rotation.X, Rotation.Y, Rotation.Z);
            return rotationMatrix;
        }

        /// <summary>
        /// Marks this particle for removal at the end of the current frame.
        /// </summary>
        public void Kill()
        {
            MarkedAsKilled = true;
        }
    }
}

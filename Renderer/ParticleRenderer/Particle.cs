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

        /// <summary>
        /// Gets or sets <b>where this particle sits in the emission order</b>: the count of particles
        /// the system spawned before it, from 0 over the system's lifetime. Use it to address a
        /// particle by its place in the sequence, which is what grid and control point placement,
        /// count remaps, sound ordering, cable chain order and snapshot row mapping all want.
        /// Never index the random table with it: consecutive particles would read consecutive slots.
        /// </summary>
        /// <remarks>
        /// The engine calls this <c>m_nUniqueParticleId</c>. Despite the name it is a plain counter,
        /// and it is <b>not</b> the engine's particle id attribute, which is <see cref="ParticleId"/>.
        /// </remarks>
        public int UniqueParticleId { get; set; }

        /// <summary>
        /// Gets or sets <b>the particle's identity for deterministic randomness</b>: its
        /// <see cref="UniqueParticleId"/> displaced by the owning system's
        /// <see cref="Utils.ParticleRandom.Seed"/>. Every random draw that must stay
        /// constant for a particle over its life is indexed from this, and the seed is what makes two
        /// instances of one effect draw differently. Never use it to order particles: the seed makes
        /// it an arbitrary large number, not a position.
        /// </summary>
        /// <remarks>
        /// This is the engine's particle id <i>attribute</i>, the one <c>PF_TYPE_PARTICLE_ID</c>
        /// reads, not its <c>m_nUniqueParticleId</c> counter, which is
        /// <see cref="UniqueParticleId"/>.
        /// </remarks>
        public int ParticleId { get; set; }

        // Varying properties (read from initializers but then change afterwards)
        /// <summary>Gets or sets the current world-space position of the particle.</summary>
        public Vector3 Position { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets the world-space position from the previous frame, used for velocity computation.</summary>
        public Vector3 PositionPrevious { get; set; } = Vector3.Zero;
        /// <summary>Gets or sets the current age of the particle in seconds.</summary>
        public float Age { get; set; } = 0f;
        /// <summary>Gets or sets the total lifetime of the particle in seconds.</summary>
        public float Lifetime { get; set; } = 1f;

        /// <summary>Gets or sets the alpha (opacity) of the particle, in the range [0, 1].</summary>
        public float Alpha { get; set; } = 1.0f;
        /// <summary>Gets or sets an alternate alpha value used by some operators and renderers.</summary>
        public float AlphaAlternate { get; set; } = 1.0f;

        /// <summary>Gets or sets the RGB color of the particle, with each component in the range [0, 1].</summary>
        public Vector3 Color { get; set; } = Vector3.One;
        /// <summary>Gets or sets the radius of the particle. The engine seeds this attribute with 5.</summary>
        public float Radius { get; set; } = 5.0f;

        /// <summary>Gets or sets the trail length multiplier for trail-based renderers. The engine seeds this attribute with 0.1.</summary>
        public float TrailLength { get; set; } = 0.1f;

        /// <summary>
        /// Gets or sets the scale factor applied to forces acting on this particle. 1 = full force,
        /// 0 = immovable (pinned). Used by movement/force operators to mask or weight forces per particle.
        /// </summary>
        public float ForceScale { get; set; } = 1.0f;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles in radians.
        /// </summary>
        public Vector3 Rotation { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public Vector3 RotationSpeed { get; set; } = Vector3.Zero;

        /// <summary>Gets or sets the current velocity of the particle.</summary>
        public Vector3 Velocity { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets the particle's normal, the engine's own attribute rather than anything derived
        /// from <see cref="Rotation"/>. A zero write is ignored, and the value is stored as given: the
        /// normal-aligned quad basis leaves its axes un-normalized, so a longer normal widens the card.
        /// </summary>
        public Vector3 Normal
        {
            readonly get => normal;
            set
            {
                if (value == Vector3.Zero)
                {
                    return;
                }

                normal = value;
            }
        }

        private Vector3 normal = new(0f, 0f, 1f);

        /// <summary>Gets the particle's age as a fraction of its lifetime. May exceed 1 if the particle outlives its lifetime.</summary>
        public readonly float NormalizedAge => Age / Math.Max(0.0001f, Lifetime); //Old version: 1 - (Lifetime / ConstantLifetime);
        /// <summary>Gets or sets the scalar speed (magnitude) of the particle; setting it rescales the velocity to the new length while preserving its direction.</summary>
        public float Speed
        {
            readonly get => Velocity.Length();
            set => Velocity = Vector3.Normalize(Velocity) * value;
        }
        /// <summary>
        /// Gets or sets the acceleration accumulated by force generators this frame; consumed and
        /// cleared by <see cref="Operators.BasicMovement"/>.
        /// </summary>
        public Vector3 ForceAccumulator { get; set; } = Vector3.Zero;

        /// <summary>
        /// Gets or sets which of the sprite sheet's animation sequences this particle plays. One sheet
        /// can carry several separate animations, and the renderers index into them with this.
        /// </summary>
        public int SequenceNumber { get; set; } = 0;

        /// <summary>Gets or sets the manually selected animation frame index.</summary>
        public int ManualAnimationFrame { get; set; } = 0;

        // Varying properties that we don't really support but are here in case they're used across operators
        /// <summary>
        /// Gets or sets the second sprite sheet sequence, which the engine's spritecard can sample
        /// alongside <see cref="SequenceNumber"/>. Authored as <c>m_nConstantSequenceNumber1</c>, so the
        /// engine's suffix is 1 where this is the second. No renderer reads it yet.
        /// </summary>
        public int SecondSequenceNumber { get; set; } = 0;

        /// <summary>Gets or sets the index of the particle's parent particle in a parent system.</summary>
        public int ParentParticleIndex { get; set; } = -1;

        /// <summary>
        /// Gets or sets the <see cref="ParticleId"/> of the parent particle this particle was created
        /// from, or -1 when it has no parent. Unlike <see cref="ParentParticleIndex"/> this survives the
        /// parent collection compacting around dead particles.
        /// </summary>
        public int ParentParticleId { get; set; } = -1;

        /// <summary>Gets or sets the identifier of the rope segment this particle belongs to.</summary>
        public int RopeSegmentId { get; set; } = 0;

        /// <summary>Gets or sets the bit field of user events currently raised on this particle.</summary>
        public int UserEventStates { get; set; } = 0;

        /// <summary>Gets or sets the minimum corner of the particle's own bounding box.</summary>
        public Vector3 BoxMins { get; set; } = Vector3.Zero;

        /// <summary>Gets or sets the maximum corner of the particle's own bounding box.</summary>
        public Vector3 BoxMaxs { get; set; } = Vector3.Zero;

        /// <summary>Gets or sets the orientation of the particle's own bounding box.</summary>
        public Vector3 BoxAngles { get; set; } = Vector3.Zero;

        /// <summary>Gets or sets the flags describing how the particle's own bounding box is used.</summary>
        public float BoxFlags { get; set; } = 0f;

        /// <summary>
        /// Gets or sets the per-segment payload carried alongside <see cref="RopeSegmentId"/>. Three
        /// components, of which the engine's debug overlay prints X and Z as a segment position and
        /// count, and <c>PF_TYPE_PARTICLE_ROPE_SEGMENT_NORMALIZED</c> reads Y.
        /// </summary>
        public Vector3 RopeSegmentData { get; set; } = Vector3.Zero;

        /// <summary>Gets or sets the alpha window threshold scratch value.</summary>
        public float AlphaWindowThreshold { get; set; } = 0f;
        /// <summary>Gets or sets the first general-purpose scratch float.</summary>
        public float ScratchFloat0 { get; set; } = 0f;
        /// <summary>Gets or sets the second general-purpose scratch float.</summary>
        public float ScratchFloat1 { get; set; } = 0f;
        /// <summary>Gets or sets the third general-purpose scratch float.</summary>
        public float ScratchFloat2 { get; set; } = 0f;
        /// <summary>
        /// Gets or sets the hitbox offset position attribute. The sequential path initializers store
        /// (path parameter, segment start control point, segment end control point) here when saving
        /// the path offset.
        /// </summary>
        public Vector3 HitboxOffsetPosition { get; set; } = Vector3.Zero;
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
                Alpha = vectorValues[3] / 255f;
            }

            Radius = parse.Float("m_flConstantRadius", Radius);
            Lifetime = parse.Float("m_flConstantLifespan", Lifetime);
            // Rotation fields are stored in radians, but the constants are authored in degrees
            Rotation = Rotation with { Z = float.DegreesToRadians(parse.Float("m_flConstantRotation", 0f)) };
            RotationSpeed = RotationSpeed with { Z = float.DegreesToRadians(parse.Float("m_flConstantRotationSpeed", 0f)) };
            Normal = parse.Vector3("m_ConstantNormal", Normal);
            SequenceNumber = parse.Int32("m_nConstantSequenceNumber", SequenceNumber);
            SecondSequenceNumber = parse.Int32("m_nConstantSequenceNumber1", SecondSequenceNumber);
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

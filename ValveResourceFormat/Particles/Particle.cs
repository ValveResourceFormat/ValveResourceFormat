using ValveKeyValue;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// A handle to one particle in a <see cref="ParticleCollection"/>, whose attributes are stored as
    /// one array per attribute. Each property returns a reference into that storage.
    /// </summary>
    public readonly struct Particle
    {
        /// <summary>Gets a shared particle with every attribute zeroed.</summary>
        public static Particle Default { get; } = Allocate();

        private readonly ParticleBuffer buffer;

        internal Particle(ParticleBuffer buffer, int index)
        {
            this.buffer = buffer;
            Index = index;
        }

        /// <summary>Creates a particle in storage of its own, with every attribute zeroed.</summary>
        internal static Particle Allocate() => new(new ParticleBuffer(1), 0);

        /// <summary>
        /// Gets or sets <b>where this particle sits in the emission order</b>: the count of particles
        /// the system spawned before it, from 0 over the system's lifetime. Use it to address a
        /// particle by its place in the sequence, which is what grid and control point placement,
        /// count remaps, sound ordering, cable chain order and snapshot row mapping all want.
        /// Never index the random table with it: consecutive particles would read consecutive slots.
        /// </summary>
        /// <remarks>
        /// Despite the name, this is a plain counter and <b>not</b> the particle id attribute,
        /// which is <see cref="ParticleId"/>.
        /// </remarks>
        public ref int UniqueParticleId => ref buffer.UniqueParticleId[Index];

        /// <summary>
        /// Gets or sets <b>the particle's identity for deterministic randomness</b>: its
        /// <see cref="UniqueParticleId"/> displaced by the owning system's
        /// <see cref="Utils.ParticleRandom.Seed"/>. Every random draw that must stay
        /// constant for a particle over its life is indexed from this, and the seed is what makes two
        /// instances of one effect draw differently. Never use it to order particles: the seed makes
        /// it an arbitrary large number, not a position.
        /// </summary>
        /// <remarks>
        /// This is the particle id <i>attribute</i>, the one <c>PF_TYPE_PARTICLE_ID</c> reads, not the
        /// spawn-order counter <see cref="UniqueParticleId"/>.
        /// </remarks>
        public ref int ParticleId => ref buffer.ParticleId[Index];

        // Varying properties (read from initializers but then change afterwards)
        /// <summary>Gets or sets the current world-space position of the particle.</summary>
        public ref Vector3 Position => ref buffer.Position[Index];
        /// <summary>
        /// Gets or sets the world-space position one step back. The particle's velocity is implicit: it is
        /// <see cref="Position"/> minus this, per step.
        /// </summary>
        public ref Vector3 PositionPrevious => ref buffer.PositionPrevious[Index];
        /// <summary>Gets or sets the current age of the particle in seconds.</summary>
        public ref float Age => ref buffer.Age[Index];
        /// <summary>Gets or sets the total lifetime of the particle in seconds.</summary>
        public ref float Lifetime => ref buffer.Lifetime[Index];

        /// <summary>Gets or sets the alpha (opacity) of the particle, in the range [0, 1].</summary>
        public ref float Alpha => ref buffer.Alpha[Index];
        /// <summary>Gets or sets an alternate alpha value used by some operators and renderers.</summary>
        public ref float AlphaAlternate => ref buffer.AlphaAlternate[Index];

        /// <summary>Gets or sets the RGB color of the particle, with each component in the range [0, 1].</summary>
        public ref Vector3 Color => ref buffer.Color[Index];
        /// <summary>Gets or sets the radius of the particle. The engine seeds this attribute with 5.</summary>
        public ref float Radius => ref buffer.Radius[Index];

        /// <summary>Gets or sets the trail length multiplier for trail-based renderers. The engine seeds this attribute with 0.1.</summary>
        public ref float TrailLength => ref buffer.TrailLength[Index];

        /// <summary>
        /// Gets or sets the scale factor applied to forces acting on this particle. 1 = full force,
        /// 0 = immovable (pinned). Used by movement/force operators to mask or weight forces per particle.
        /// </summary>
        public ref float ForceScale => ref buffer.ForceScale[Index];

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles in radians.
        /// </summary>
        public ref Vector3 Rotation => ref buffer.Rotation[Index];

        /// <summary>
        /// Gets or sets (Yaw, Pitch, Roll) Euler angles rotation speed.
        /// </summary>
        public ref Vector3 RotationSpeed => ref buffer.RotationSpeed[Index];

        /// <summary>
        /// Gets or sets the particle's normal, the engine's own attribute rather than anything derived
        /// from <see cref="Rotation"/>. The value is stored as given, zero included: the normal-aligned
        /// quad basis leaves its axes un-normalized, so a longer normal widens the card.
        /// </summary>
        public ref Vector3 Normal => ref buffer.Normal[Index];

        /// <summary>
        /// The particle's velocity in units per second, from its last step taken as
        /// <paramref name="frameTime"/> seconds long. Zero for a step of no length.
        /// </summary>
        public Vector3 GetVelocity(float frameTime)
            => frameTime > 0f ? (Position - PositionPrevious) / frameTime : Vector3.Zero;

        /// <summary>Gets the particle's age as a fraction of its lifetime. May exceed 1 if the particle outlives its lifetime.</summary>
        public float NormalizedAge => Age / Math.Max(0.0001f, Lifetime); //Old version: 1 - (Lifetime / ConstantLifetime);
        /// <summary>
        /// Gets or sets the acceleration accumulated by force generators this frame; consumed and
        /// cleared by <see cref="Operators.BasicMovement"/>.
        /// </summary>
        public ref Vector3 ForceAccumulator => ref buffer.ForceAccumulator[Index];

        /// <summary>
        /// Gets or sets which of the sprite sheet's animation sequences this particle plays. One sheet
        /// can carry several separate animations, and the renderers index into them with this. Stored as
        /// a float like every other attribute, so a fractional value survives until it is read as an index.
        /// </summary>
        public ref float SequenceNumber => ref buffer.SequenceNumber[Index];

        /// <summary>
        /// Gets or sets the sprite sheet playback position an operator drives by hand, in sequence
        /// loops: 0.5 is halfway through the sequence and 2.0 has played it twice.
        /// </summary>
        public ref float ManualAnimationFrame => ref buffer.ManualAnimationFrame[Index];

        // Varying properties that we don't really support but are here in case they're used across operators
        /// <summary>
        /// Gets or sets the second sprite sheet sequence, which spritecard can sample alongside
        /// <see cref="SequenceNumber"/>.
        /// Authored as <c>m_nConstantSequenceNumber1</c>, where the 1 suffix marks the second sequence.
        /// No renderer reads it yet.
        /// </summary>
        public ref float SecondSequenceNumber => ref buffer.SecondSequenceNumber[Index];

        /// <summary>Gets or sets the index of the particle's parent particle in a parent system.</summary>
        public ref int ParentParticleIndex => ref buffer.ParentParticleIndex[Index];

        /// <summary>
        /// Gets or sets the <see cref="ParticleId"/> of the parent particle this particle was created
        /// from, or <see cref="int.MinValue"/> when it has no parent. Unlike <see cref="ParentParticleIndex"/>
        /// this survives the parent collection compacting around dead particles.
        /// </summary>
        public ref int ParentParticleId => ref buffer.ParentParticleId[Index];

        /// <summary>Gets or sets the identifier of the rope segment this particle belongs to.</summary>
        public ref int RopeSegmentId => ref buffer.RopeSegmentId[Index];

        /// <summary>Gets or sets the bit field of user events currently raised on this particle.</summary>
        public ref int UserEventStates => ref buffer.UserEventStates[Index];

        /// <summary>Gets or sets the minimum corner of the particle's own bounding box.</summary>
        public ref Vector3 BoxMins => ref buffer.BoxMins[Index];

        /// <summary>Gets or sets the maximum corner of the particle's own bounding box.</summary>
        public ref Vector3 BoxMaxs => ref buffer.BoxMaxs[Index];

        /// <summary>Gets or sets the orientation of the particle's own bounding box.</summary>
        public ref Vector3 BoxAngles => ref buffer.BoxAngles[Index];

        /// <summary>Gets or sets the flags describing how the particle's own bounding box is used.</summary>
        public ref float BoxFlags => ref buffer.BoxFlags[Index];

        /// <summary>
        /// Gets or sets the per-segment payload carried alongside <see cref="RopeSegmentId"/>.
        /// X and Z are a segment position and count, and <c>PF_TYPE_PARTICLE_ROPE_SEGMENT_NORMALIZED</c>
        /// reads Y.
        /// </summary>
        public ref Vector3 RopeSegmentData => ref buffer.RopeSegmentData[Index];

        /// <summary>Gets or sets the alpha window threshold scratch value.</summary>
        public ref float AlphaWindowThreshold => ref buffer.AlphaWindowThreshold[Index];
        /// <summary>Gets or sets the first general-purpose scratch float.</summary>
        public ref float ScratchFloat0 => ref buffer.ScratchFloat0[Index];
        /// <summary>Gets or sets the second general-purpose scratch float.</summary>
        public ref float ScratchFloat1 => ref buffer.ScratchFloat1[Index];
        /// <summary>Gets or sets the third general-purpose scratch float.</summary>
        public ref float ScratchFloat2 => ref buffer.ScratchFloat2[Index];
        /// <summary>
        /// Gets or sets the hitbox offset position attribute. The sequential path initializers store
        /// (path parameter, segment start control point, segment end control point) here when saving
        /// the path offset.
        /// </summary>
        public ref Vector3 HitboxOffsetPosition => ref buffer.HitboxOffsetPosition[Index];

        /// <summary>
        /// Gets or sets the index of the model hitbox, or bone, the particle is placed on. The functions
        /// that place particles on a model write it with <see cref="HitboxOffsetPosition"/> holding the
        /// position inside that hitbox's box, as a fraction of its extent per axis.
        /// </summary>
        public ref int HitboxIndex => ref buffer.HitboxIndex[Index];

        /// <summary>
        /// Gets or sets the four model bones a skinned particle follows, as 16-bit indices from the low
        /// bits up. 0xFFFF marks an unused slot. As a vector field it reads as the bits of the low and
        /// high halves in X and Y.
        /// </summary>
        internal ref ulong BoneIndices => ref buffer.BoneIndices[Index];

        /// <summary>
        /// Gets or sets the weights of the first three of <see cref="BoneIndices"/>. The fourth bone
        /// takes what is left of 1.
        /// </summary>
        public ref Vector3 BoneWeights => ref buffer.BoneWeights[Index];
        /// <summary>Gets or sets a general-purpose scratch vector.</summary>
        public ref Vector3 ScratchVector => ref buffer.ScratchVector[Index];
        /// <summary>Gets or sets a second general-purpose scratch vector.</summary>
        public ref Vector3 ScratchVector2 => ref buffer.ScratchVector2[Index];
        /// <summary>Gets or sets the glow color attribute.</summary>
        public ref Vector3 GlowRgb => ref buffer.GlowRgb[Index];
        /// <summary>Gets or sets the glow alpha attribute.</summary>
        public ref float GlowAlpha => ref buffer.GlowAlpha[Index];
        /// <summary>Gets or sets the first free value handed to the renderer's shader.</summary>
        public ref float ShaderExtraData1 => ref buffer.ShaderExtraData1[Index];
        /// <summary>Gets or sets the second free value handed to the renderer's shader.</summary>
        public ref float ShaderExtraData2 => ref buffer.ShaderExtraData2[Index];
        /// <summary>Gets or sets the system time at which this particle was created.</summary>
        public ref float CreationTime => ref buffer.CreationTime[Index];

        /// <summary>Gets or sets a value indicating whether this particle has been marked for removal.</summary>
        public ref bool MarkedAsKilled => ref buffer.MarkedAsKilled[Index];
        /// <summary>Gets the index of this particle within its collection's arrays.</summary>
        public int Index { get; }

        /// <summary>Gets the storage this particle lives in.</summary>
        internal ParticleBuffer Buffer => buffer;

        /// <summary>Gets a span holding just this particle.</summary>
        internal ParticleSpan AsSpan() => new(buffer, Index, 1);

        /// <summary>
        /// Creates the particle every new particle of a system starts as: the attribute defaults with the
        /// constant attributes of the definition on top.
        /// </summary>
        internal static Particle CreateConstants(ParticleDefinitionParser parse)
        {
            var particle = Allocate();
            particle.buffer.SetDefaults(0);
            particle.ApplyConstants(parse);
            return particle;
        }

        private void ApplyConstants(ParticleDefinitionParser parse)
        {
            if (parse.Data.TryGetValue("m_ConstantColor", out var constantColor))
            {
                Color = constantColor.ToVector3() / 255f;
                if (constantColor.Count > 3)
                {
                    var alpha = constantColor[3];
                    Alpha = alpha.ValueType == KVValueType.Null ? 0f : (int)alpha / 255f;
                }
            }

            Radius = parse.Float("m_flConstantRadius", Radius);
            Lifetime = parse.Float("m_flConstantLifespan", Lifetime);
            // Unlike every other source of these attributes, the constants are not converted from degrees
            Rotation = Rotation with { Z = parse.Float("m_flConstantRotation", 0f) };
            RotationSpeed = RotationSpeed with { Z = parse.Float("m_flConstantRotationSpeed", 0f) };
            Normal = parse.Vector3("m_ConstantNormal", Normal);
            SequenceNumber = parse.Float("m_nConstantSequenceNumber", SequenceNumber);
            SecondSequenceNumber = parse.Float("m_nConstantSequenceNumber1", SecondSequenceNumber);
        }

        /// <summary>
        /// Returns a combined scale-translation matrix for this particle.
        /// </summary>
        /// <param name="radiusScale">Optional additional scale factor applied to the radius.</param>
        public Matrix4x4 GetTransformationMatrix(float radiusScale = 1f)
        {
            var scaleMatrix = Matrix4x4.CreateScale(Radius * radiusScale);
            var translationMatrix = Matrix4x4.CreateTranslation(Position.X, Position.Y, Position.Z);

            return Matrix4x4.Multiply(scaleMatrix, translationMatrix);
        }

        /// <summary>
        /// Returns a rotation matrix derived from the particle's Euler angles.
        /// </summary>
        public Matrix4x4 GetRotationMatrix()
            => EntityTransformHelper.EulerAnglesToRotationMatrixRadians(new Vector3(Rotation.Y, Rotation.X, Rotation.Z));

        /// <summary>
        /// Marks this particle for removal once the running function returns.
        /// </summary>
        public void Kill()
        {
            MarkedAsKilled = true;
        }
    }
}

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// A contiguous run of particles in a <see cref="ParticleCollection"/>, handing out a
    /// <see cref="Particle"/> handle for each.
    /// </summary>
    public readonly struct ParticleSpan
    {
        private readonly ParticleBuffer? buffer;
        private readonly int start;

        internal ParticleSpan(ParticleBuffer buffer, int start, int length)
        {
            this.buffer = buffer;
            this.start = start;
            Length = length;
        }

        /// <summary>Gets the number of particles in the span.</summary>
        public int Length { get; }

        /// <summary>Gets a value indicating whether the span holds no particles.</summary>
        public bool IsEmpty => Length == 0;

        /// <summary>Gets the storage index of the first particle in the span.</summary>
        internal int Start => start;

        /// <summary>Gets the storage the particles live in.</summary>
        internal ParticleBuffer? Buffer => buffer;

        /// <summary>Gets the particle at <paramref name="index"/> within the span.</summary>
        public Particle this[int index]
        {
            get
            {
                if ((uint)index >= (uint)Length)
                {
                    throw new ArgumentOutOfRangeException(nameof(index));
                }

                return new(buffer!, start + index);
            }
        }

        /// <summary>Gets the particle at <paramref name="index"/> within the span.</summary>
        public Particle this[Index index] => this[index.GetOffset(Length)];

        /// <summary>Gets the particles in <paramref name="range"/> of the span.</summary>
        public ParticleSpan this[Range range]
        {
            get
            {
                var (offset, length) = range.GetOffsetAndLength(Length);
                return Slice(offset, length);
            }
        }

        /// <summary>Forms a slice from <paramref name="offset"/> to the end of the span.</summary>
        public ParticleSpan Slice(int offset) => Slice(offset, Length - offset);

        /// <summary>Forms a slice of <paramref name="length"/> particles from <paramref name="offset"/>.</summary>
        public ParticleSpan Slice(int offset, int length)
        {
            if ((uint)offset > (uint)Length || (uint)length > (uint)(Length - offset))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return length == 0 ? default : new(buffer!, start + offset, length);
        }

        // One attribute of every particle in the span, for loops that run over a single attribute
        internal Span<int> UniqueParticleId => Column(buffer?.UniqueParticleId);
        internal Span<int> ParticleId => Column(buffer?.ParticleId);
        internal Span<Vector3> Position => Column(buffer?.Position);
        internal Span<Vector3> PositionPrevious => Column(buffer?.PositionPrevious);
        internal Span<float> Age => Column(buffer?.Age);
        internal Span<float> Lifetime => Column(buffer?.Lifetime);
        internal Span<float> Alpha => Column(buffer?.Alpha);
        internal Span<float> AlphaAlternate => Column(buffer?.AlphaAlternate);
        internal Span<Vector3> Color => Column(buffer?.Color);
        internal Span<float> Radius => Column(buffer?.Radius);
        internal Span<float> TrailLength => Column(buffer?.TrailLength);
        internal Span<float> ForceScale => Column(buffer?.ForceScale);
        internal Span<Vector3> Rotation => Column(buffer?.Rotation);
        internal Span<Vector3> RotationSpeed => Column(buffer?.RotationSpeed);
        internal Span<Vector3> Normal => Column(buffer?.Normal);
        internal Span<Vector3> ForceAccumulator => Column(buffer?.ForceAccumulator);
        internal Span<float> SequenceNumber => Column(buffer?.SequenceNumber);
        internal Span<float> ManualAnimationFrame => Column(buffer?.ManualAnimationFrame);
        internal Span<float> SecondSequenceNumber => Column(buffer?.SecondSequenceNumber);
        internal Span<int> ParentParticleIndex => Column(buffer?.ParentParticleIndex);
        internal Span<int> ParentParticleId => Column(buffer?.ParentParticleId);
        internal Span<int> RopeSegmentId => Column(buffer?.RopeSegmentId);
        internal Span<int> UserEventStates => Column(buffer?.UserEventStates);
        internal Span<Vector3> BoxMins => Column(buffer?.BoxMins);
        internal Span<Vector3> BoxMaxs => Column(buffer?.BoxMaxs);
        internal Span<Vector3> BoxAngles => Column(buffer?.BoxAngles);
        internal Span<float> BoxFlags => Column(buffer?.BoxFlags);
        internal Span<Vector3> RopeSegmentData => Column(buffer?.RopeSegmentData);
        internal Span<float> AlphaWindowThreshold => Column(buffer?.AlphaWindowThreshold);
        internal Span<float> ScratchFloat0 => Column(buffer?.ScratchFloat0);
        internal Span<float> ScratchFloat1 => Column(buffer?.ScratchFloat1);
        internal Span<float> ScratchFloat2 => Column(buffer?.ScratchFloat2);
        internal Span<Vector3> HitboxOffsetPosition => Column(buffer?.HitboxOffsetPosition);
        internal Span<int> HitboxIndex => Column(buffer?.HitboxIndex);
        internal Span<ulong> BoneIndices => Column(buffer?.BoneIndices);
        internal Span<Vector3> BoneWeights => Column(buffer?.BoneWeights);
        internal Span<Vector3> ScratchVector => Column(buffer?.ScratchVector);
        internal Span<Vector3> ScratchVector2 => Column(buffer?.ScratchVector2);
        internal Span<Vector3> GlowRgb => Column(buffer?.GlowRgb);
        internal Span<float> GlowAlpha => Column(buffer?.GlowAlpha);
        internal Span<float> ShaderExtraData1 => Column(buffer?.ShaderExtraData1);
        internal Span<float> ShaderExtraData2 => Column(buffer?.ShaderExtraData2);
        internal Span<float> CreationTime => Column(buffer?.CreationTime);
        internal Span<bool> MarkedAsKilled => Column(buffer?.MarkedAsKilled);

        private Span<T> Column<T>(T[]? attribute) => attribute.AsSpan(start, Length);

        /// <summary>Returns an enumerator over the particles of the span.</summary>
        public Enumerator GetEnumerator() => new(this);

        /// <summary>Enumerates the particles of a <see cref="ParticleSpan"/>.</summary>
        public struct Enumerator
        {
            private readonly ParticleBuffer? buffer;
            private readonly int end;
            private int index;

            internal Enumerator(ParticleSpan span)
            {
                buffer = span.buffer;
                index = span.start - 1;
                end = span.start + span.Length;
            }

            /// <summary>Gets the particle at the current position.</summary>
            public readonly Particle Current => new(buffer!, index);

            /// <summary>Advances to the next particle.</summary>
            public bool MoveNext() => ++index < end;
        }
    }
}

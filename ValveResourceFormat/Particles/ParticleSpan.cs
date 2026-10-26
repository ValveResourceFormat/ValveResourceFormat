using System.Numerics.Tensors;
using System.Runtime.InteropServices;

namespace ValveResourceFormat.Particles
{
    /// <summary>
    /// A contiguous run of particles in a <see cref="ParticleCollection"/>, read and written one
    /// attribute column at a time. A default instance is empty and has no columns to read.
    /// </summary>
    public readonly struct ParticleSpan
    {
        private readonly ParticleBuffer buffer;
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

        /// <summary>Gets the particles in <paramref name="range"/> of the span.</summary>
        public ParticleSpan this[Range range]
        {
            get
            {
                var (offset, length) = range.GetOffsetAndLength(Length);
                return Slice(offset, length);
            }
        }

        /// <summary>Forms a slice of <paramref name="length"/> particles from <paramref name="offset"/>.</summary>
        public ParticleSpan Slice(int offset, int length)
        {
            if ((uint)offset > (uint)Length || (uint)length > (uint)(Length - offset))
            {
                throw new ArgumentOutOfRangeException(nameof(offset));
            }

            return new(buffer, start + offset, length);
        }

        /// <summary>Forms the span from this one's first particle to the collection index <paramref name="end"/>.</summary>
        internal ParticleSpan WithEnd(int end) => new(buffer, start, end - start);

        /// <summary>
        /// Enumerates the span block by block of four by storage index, the unit a varying random input
        /// draws once for, as <c>foreach (var (offset, block) in span.Blocks())</c>, each block with its
        /// offset in the span.
        /// </summary>
        internal BlockEnumerator Blocks() => new(this);

        /// <summary>
        /// Gets the block of four by storage index that holds the particle at <paramref name="index"/>,
        /// as <see cref="Blocks"/> enumerates it, with its offset in the span.
        /// </summary>
        internal (int Offset, ParticleSpan Block) BlockAt(int index)
        {
            var offset = Math.Max(0, index - ((start + index) & 3));
            var length = Math.Min(4 - ((start + offset) & 3), Length - offset);
            return (offset, Slice(offset, length));
        }

        /// <summary>
        /// Evaluates two vector inputs for every particle in the span into scratch columns, as a function
        /// that sets up the first and then the second, and evaluates them in that order in each block.
        /// </summary>
        internal void FillVectorInputs(IVectorProvider first, IVectorProvider second, ParticleSystemState particleSystemState,
            out Span<Vector3> firstValues, out Span<Vector3> secondValues)
        {
            firstValues = first.Prepare(this, particleSystemState);
            secondValues = second.Prepare(this, particleSystemState);

            foreach (var (offset, block, lane) in InputBlocks())
            {
                first.FillPerBlock(block, lane, particleSystemState, firstValues[offset..]);
                second.FillPerBlock(block, lane, particleSystemState, secondValues[offset..]);
            }
        }

        /// <summary>
        /// Enumerates the blocks of four a function walks its vector inputs in, as
        /// <c>foreach (var (offset, block, lane) in span.InputBlocks())</c>, each block with its offset in
        /// the span and the lane its first particle reads. The blocks are taken by storage index, or with
        /// <paramref name="blocksFromStart"/> from the span's first particle, as a function counts them
        /// for inputs it set up from the first block of the collection.
        /// </summary>
        internal InputBlockEnumerator InputBlocks(bool blocksFromStart = false) => new(this, blocksFromStart);

        /// <summary>Walks a span in blocks of four by storage index.</summary>
        internal struct BlockEnumerator
        {
            private readonly ParticleSpan span;
            private int offset;
            private int length;

            internal BlockEnumerator(ParticleSpan span)
            {
                this.span = span;
            }

            /// <summary>Gets the block at the current position and its offset in the span.</summary>
            public readonly (int Offset, ParticleSpan Block) Current => (offset, span.Slice(offset, length));

            /// <summary>Returns this enumerator, for <c>foreach</c>.</summary>
            public readonly BlockEnumerator GetEnumerator() => this;

            /// <summary>Moves to the next block.</summary>
            public bool MoveNext()
            {
                offset += length;

                if (offset >= span.Length)
                {
                    return false;
                }

                length = Math.Min(4 - ((span.start + offset) & 3), span.Length - offset);
                return true;
            }
        }

        /// <summary>Walks a span in the blocks of four a function evaluates its vector inputs in.</summary>
        internal struct InputBlockEnumerator
        {
            private readonly ParticleSpan span;
            private readonly bool blocksFromStart;
            private int offset;
            private int length;
            private int lane;

            internal InputBlockEnumerator(ParticleSpan span, bool blocksFromStart)
            {
                this.span = span;
                this.blocksFromStart = blocksFromStart;
            }

            /// <summary>Gets the block at the current position, its offset in the span and the lane of its first particle.</summary>
            public readonly (int Offset, ParticleSpan Block, int Lane) Current => (offset, span.Slice(offset, length), lane);

            /// <summary>Returns this enumerator, for <c>foreach</c>.</summary>
            public readonly InputBlockEnumerator GetEnumerator() => this;

            /// <summary>Moves to the next block.</summary>
            public bool MoveNext()
            {
                offset += length;

                if (offset >= span.Length)
                {
                    return false;
                }

                lane = blocksFromStart ? 0 : (span.start + offset) & 3;
                length = Math.Min(4 - lane, span.Length - offset);
                return true;
            }
        }

        /// <summary>Gets the system time the ages of the particles are measured against.</summary>
        public float Time => buffer.Time;

        /// <summary>
        /// Gets the float <paramref name="field"/> of every particle in the span. The field must be stored;
        /// see <see cref="TryGetFloats"/> for one that may be a constant.
        /// </summary>
        internal Span<float> Floats(ParticleField field)
        {
            var column = buffer.Scalars[(int)field] ?? throw NotStored(field);
            return column.AsSpan(start, Length);
        }

        /// <summary>Gets the integer <paramref name="field"/> of every particle in the span. The field must be stored.</summary>
        internal Span<int> Integers(ParticleField field) => MemoryMarshal.Cast<float, int>(Floats(field));

        /// <summary>
        /// Gets the vector <paramref name="field"/> of every particle in the span. The field must be stored;
        /// see <see cref="TryGetVectors"/> for one that may be a constant.
        /// </summary>
        internal Span<Vector3> Vectors(ParticleField field)
        {
            var column = buffer.Vectors[(int)field] ?? throw NotStored(field);
            return column.AsSpan(start, Length);
        }

        private static InvalidOperationException NotStored(ParticleField field)
            => new($"Particle attribute {field} is not stored by this system, so it has no column; read its constant instead.");

        /// <summary>
        /// Gets the float <paramref name="field"/> of every particle in the span for reading, an integer
        /// attribute as its bits. A field the system does not store reads as a scratch column holding its
        /// constant, valid until the running function returns.
        /// </summary>
        internal ReadOnlySpan<float> ReadFloats(ParticleField field)
        {
            if (TryGetFloats(field, out var values, out var constant))
            {
                return values;
            }

            var column = ParticleScratch.Floats(Length);
            column.Fill(constant);
            return column;
        }

        /// <summary>Gets the integer <paramref name="field"/> of every particle in the span for reading, as <see cref="ReadFloats"/> does.</summary>
        internal ReadOnlySpan<int> ReadIntegers(ParticleField field) => MemoryMarshal.Cast<float, int>(ReadFloats(field));

        /// <summary>Gets the vector <paramref name="field"/> of every particle in the span for reading, as <see cref="ReadFloats"/> does.</summary>
        internal ReadOnlySpan<Vector3> ReadVectors(ParticleField field)
        {
            if (TryGetVectors(field, out var values, out var constant))
            {
                return values;
            }

            var column = ParticleScratch.Vectors(Length);
            column.Fill(constant);
            return column;
        }

        /// <summary>
        /// Gets the float <paramref name="field"/> of the particle at <paramref name="index"/>, an integer
        /// attribute as its bits, or the field's constant when it is not stored.
        /// </summary>
        public float ReadFloat(ParticleField field, int index)
            => TryGetFloats(field, out var values, out var constant) ? values[index] : constant;

        /// <summary>Gets the integer <paramref name="field"/> of the particle at <paramref name="index"/>, as <see cref="ReadFloat"/> does.</summary>
        public int ReadInteger(ParticleField field, int index) => BitConverter.SingleToInt32Bits(ReadFloat(field, index));

        /// <summary>Gets the vector <paramref name="field"/> of the particle at <paramref name="index"/>, or the field's constant when it is not stored.</summary>
        public Vector3 ReadVector(ParticleField field, int index)
            => TryGetVectors(field, out var values, out var constant) ? values[index] : constant;

        /// <summary>Gets the bone indices of the particle at <paramref name="index"/>, or their constant when they are not stored.</summary>
        internal ulong ReadBoneIndices(int index) => buffer.BoneIndices is { } column ? column[start + index] : buffer.Constants.BoneIndices;

        /// <summary>Whether the particles hold their own bone indices and bone weights rather than the constants.</summary>
        internal bool HasBones => buffer.BoneIndices != null && buffer.Vectors[(int)ParticleField.BoneWeights] != null;

        /// <summary>
        /// Gets the bone indices of every particle in the span for reading. When they are not stored they
        /// read as a scratch column holding their constant, valid until the running function returns.
        /// </summary>
        internal ReadOnlySpan<ulong> ReadBoneIndices()
        {
            if (buffer.BoneIndices is { } column)
            {
                return column.AsSpan(start, Length);
            }

            var constants = ParticleScratch.BoneIndices(Length);
            constants.Fill(buffer.Constants.BoneIndices);
            return constants;
        }

        /// <summary>
        /// Gets the bone indices of every particle in the span. They must be stored; see
        /// <see cref="ReadBoneIndices()"/> for reading them when they may be a constant.
        /// </summary>
        internal Span<ulong> BoneIndices
        {
            get
            {
                var column = buffer.BoneIndices ?? throw NotStored(ParticleField.BoneIndices);
                return column.AsSpan(start, Length);
            }
        }

        /// <summary>Gets the age of every particle in the span, in a scratch column valid until the running function returns.</summary>
        internal Span<float> ReadAges()
        {
            var ages = ParticleScratch.Floats(Length);
            TensorPrimitives.Subtract(Time, Floats(ParticleField.CreationTime), ages);
            return ages;
        }

        /// <summary>
        /// Gets the scalar <paramref name="field"/> of every particle in the span as functions read and
        /// write it, an integer attribute as its bits. Returns false when the field has no column:
        /// <paramref name="constant"/> is then the value every particle has, or 0 for a field that is
        /// not a scalar attribute.
        /// </summary>
        internal bool TryGetFloats(ParticleField field, out Span<float> values, out float constant)
        {
            values = default;
            constant = 0f;

            if ((uint)field >= ParticleBuffer.FieldCount)
            {
                return false;
            }

            if (buffer.Scalars[(int)field] is not { } scalars)
            {
                constant = buffer.Constants.Scalars[(int)field];
                return false;
            }

            values = scalars.AsSpan(start, Length);
            return true;
        }

        /// <summary>
        /// Gets the vector <paramref name="field"/> of every particle in the span. Returns false when the
        /// field has no column: <paramref name="constant"/> is then the value every particle has, or zero
        /// for a field that is not a vector attribute.
        /// </summary>
        internal bool TryGetVectors(ParticleField field, out Span<Vector3> values, out Vector3 constant)
        {
            values = default;
            constant = Vector3.Zero;

            if ((uint)field >= ParticleBuffer.FieldCount)
            {
                return false;
            }

            if (buffer.Vectors[(int)field] is not { } vectors)
            {
                constant = buffer.Constants.Vectors[(int)field];
                return false;
            }

            values = vectors.AsSpan(start, Length);
            return true;
        }
    }
}

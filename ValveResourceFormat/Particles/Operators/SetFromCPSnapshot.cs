using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Each frame, writes a particle attribute from a control-point snapshot. Used (for example) to keep
    /// rope/cable pin flags (<see cref="ParticleField.ForceScale"/>) asserted so pinned particles stay
    /// immovable as the simulation runs.
    /// </summary>
    /// <remarks>
    /// Rows are handed out in particle order every step, starting again from the start point: one row
    /// after another, wrapping back to the start point, or stepping by the increment, reversed or
    /// drawn at random. Vector values blend into the attribute by the interpolation input, and a
    /// fractional start point with <c>m_bSubSample</c> blends between neighbouring rows.
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_SetFromCPSnapshot">C_OP_SetFromCPSnapshot</seealso>
    class SetFromCPSnapshot : ParticleFunctionOperator
    {
        private readonly SnapshotRead snapshot;
        private readonly int localSpaceCp;
        private readonly bool random;
        private readonly bool reverse;
        private readonly int randomSeed;
        private readonly INumberProvider startPoint = new LiteralNumberProvider(0);
        private readonly INumberProvider increment = new LiteralNumberProvider(1);
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1);
        private readonly bool subSample;
        private readonly bool writePrevious = true;

        private int sampleCounter;

        public SetFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SnapshotRead(parse);
            localSpaceCp = Math.Clamp(snapshot.LocalSpaceCP, -1, ParticleSystemState.MaxControlPoint);
            random = parse.Boolean("m_bRandom", random);
            reverse = parse.Boolean("m_bReverse", reverse);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            startPoint = parse.NumberProvider("m_nSnapShotStartPoint", startPoint);
            increment = parse.NumberProvider("m_nSnapShotIncrement", increment);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);
            subSample = parse.Boolean("m_bSubSample", subSample);
            writePrevious = parse.Boolean("m_bPrev", writePrevious);
        }

        public override void Reset()
        {
            sampleCounter = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        /// <inheritdoc/>
        public override ulong StoredFields => snapshot.AttributeToWrite == ParticleField.Position
            ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            : snapshot.AttributeToWrite.Mask();

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

            var current = particles.Current;
            var blends = interpolation.Fill(current, particleSystemState);

            if (column == null || rowCount == 0)
            {
                return;
            }

            var start = MathF.Max(0f, MathF.Min(rowCount - 1, startPoint.NextNumber(particleSystemState)));
            var step = Math.Max(1, (int)increment.NextNumber(particleSystemState));
            var indexed = reverse || random || step != 1;
            var firstRow = (int)MathF.Ceiling(start);
            var subSampleOffset = firstRow - (int)MathF.Floor(start);
            var fraction = start - MathF.Floor(start);
            var cursor = firstRow;

            var field = snapshot.AttributeToWrite;
            var storage = ParticleBuffer.StorageOf(field);
            var vectors = column as Vector3[];
            var floats = column as float[];
            var integers = column as int[];

            var writesFloats = storage is ParticleFieldStorage.Scalar or ParticleFieldStorage.Integer;
            var writesVectors = storage == ParticleFieldStorage.Vector;

            // Bone indices and handles cannot be written from snapshot rows, so no rows are read
            if (!writesFloats && !writesVectors)
            {
                return;
            }

            Span<float> floatOutput = default;
            Span<Vector3> vectorOutput = default;
            Span<Vector3> previousOutput = default;
            ReadOnlySpan<Vector3> positions = default;

            if (writesFloats)
            {
                floatOutput = current.Floats(field);
            }

            if (writesVectors)
            {
                vectorOutput = current.Vectors(field);

                if (writePrevious && field == ParticleField.Position)
                {
                    previousOutput = current.Vectors(ParticleField.PositionPrevious);
                }

                if (field == ParticleField.PositionPrevious)
                {
                    positions = current.ReadVectors(ParticleField.Position);
                }
            }

            for (var i = 0; i < current.Length; i++)
            {
                int row;
                var walked = cursor;

                if (!indexed)
                {
                    row = cursor;
                    cursor = cursor + 1 < rowCount ? cursor + 1 : firstRow;
                }
                else if (random)
                {
                    var sample = particleSystemState.Random.NextOrSeeded(randomSeed, ref sampleCounter);

                    row = Math.Min((int)(rowCount * sample), rowCount - 1);
                }
                else
                {
                    // Vector rows advance by (cursor + step - 1) % rowCount + 1 and the read index is only wrapped for a subset,
                    // so one row past the end is read each cycle (row 3 when reversed) and the first is skipped after
                    // the first pass; the cursor wraps cleanly here
                    cursor %= rowCount;
                    walked = cursor;
                    row = reverse ? rowCount - cursor - 1 : cursor;
                    cursor = (cursor + step) % rowCount;
                }

                if (writesFloats)
                {
                    if (floats != null && (uint)row < (uint)floats.Length)
                    {
                        floatOutput[i] = floats[row];
                    }
                    else if (integers != null && (uint)row < (uint)integers.Length)
                    {
                        floatOutput[i] = BitConverter.Int32BitsToSingle(integers[row]);
                    }

                    continue;
                }

                if (vectors == null || (uint)row >= (uint)vectors.Length)
                {
                    continue;
                }

                var value = ReadVector(vectors, row, positions, i, frameTime, particleSystemState);

                // Random, reversed and stepped rows are sub-sampled too, but always against row floor(start point)
                // (mirrored when reversed), so with a whole-number start every particle gets that one row.
                // Each row blends toward its own neighbour here, and random rows are not blended
                if (subSample && !random)
                {
                    var neighbour = MathUtils.WrapIndex(walked - subSampleOffset, rowCount);
                    var other = ReadVector(vectors, reverse ? rowCount - neighbour - 1 : neighbour, positions, i, frameTime, particleSystemState);
                    value = MathUtils.Lerp(other, value, fraction);
                }

                var blend = blends[i];
                vectorOutput[i] = MathUtils.Lerp(vectorOutput[i], value, blend);

                if (!previousOutput.IsEmpty)
                {
                    previousOutput[i] = MathUtils.Lerp(previousOutput[i], value, blend);
                }
            }
        }

        /// <summary>
        /// A snapshot row moved into the local space control point's frame. A previous position row
        /// is a velocity, turned into the position one step back from the current one.
        /// </summary>
        private Vector3 ReadVector(Vector3[] vectors, int row, ReadOnlySpan<Vector3> positions, int index, float frameTime, ParticleSystemState particleSystemState)
        {
            var value = vectors[row];
            var field = snapshot.AttributeToWrite;

            if (localSpaceCp != -1)
            {
                value = field switch
                {
                    ParticleField.Position
                        => ControlPointTransformProvider.TransformPosition(particleSystemState, localSpaceCp, value),
                    ParticleField.PositionPrevious or ParticleField.Normal
                        => ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCp, value),
                    _ => value,
                };
            }

            if (field == ParticleField.PositionPrevious)
            {
                var dt = frameTime > 0f ? frameTime : 1f / 30f;
                value = positions[index] - (value * dt);
            }

            return value;
        }
    }
}

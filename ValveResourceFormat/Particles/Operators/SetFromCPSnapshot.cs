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

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

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
            var fieldType = field.FieldType();
            var vectors = column as Vector3[];
            var floats = column as float[];

            foreach (var particle in particles.Current)
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
                    cursor %= rowCount;
                    walked = cursor;
                    row = reverse ? rowCount - cursor - 1 : cursor;
                    cursor = (cursor + step) % rowCount;
                }

                if (fieldType == "float")
                {
                    if (floats != null && (uint)row < (uint)floats.Length)
                    {
                        particle.SetScalar(field, floats[row]);
                    }

                    continue;
                }

                if (fieldType != "vector" || vectors == null || (uint)row >= (uint)vectors.Length)
                {
                    continue;
                }

                var value = ReadVector(particle, vectors, row, frameTime, particleSystemState);

                if (subSample && !random)
                {
                    var neighbour = MathUtils.WrapIndex(walked - subSampleOffset, rowCount);
                    var other = ReadVector(particle, vectors, reverse ? rowCount - neighbour - 1 : neighbour, frameTime, particleSystemState);
                    value = MathUtils.Lerp(other, value, fraction);
                }

                var blend = interpolation.NextNumber(particle, particleSystemState);
                particle.SetVector(field, MathUtils.Lerp(particle.GetVector(field), value, blend));

                if (writePrevious && field == ParticleField.Position)
                {
                    particle.PositionPrevious = MathUtils.Lerp(particle.PositionPrevious, value, blend);
                }
            }
        }

        /// <summary>
        /// A snapshot row moved into the local space control point's frame. A previous position row
        /// is a velocity, turned into the position one step back from the current one.
        /// </summary>
        private Vector3 ReadVector(Particle particle, Vector3[] vectors, int row, float frameTime, ParticleSystemState particleSystemState)
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
                value = particle.Position - (value * dt);
            }

            return value;
        }
    }
}

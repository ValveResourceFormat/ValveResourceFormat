using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Sets a particle attribute at spawn from a control point snapshot. A manual index reads a fixed
    /// row, <c>m_bRandom</c> draws one, and otherwise a cursor walks the rows by the increment,
    /// reversed if asked, and carries over between emissions. A vector row moves the cursor on by
    /// twice the increment less one, a scalar row by the increment.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitFromCPSnapshot">C_INIT_InitFromCPSnapshot</seealso>
    class InitFromCPSnapshot : ParticleFunctionInitializer
    {
        private readonly SnapshotRead snapshot;
        private readonly bool localSpaceAngles;
        private readonly bool random;
        private readonly bool reverse;
        private readonly int randomSeed;
        private readonly bool resetIndexOnChanges;
        private readonly INumberProvider increment = new LiteralNumberProvider(1f);
        private readonly INumberProvider manualIndex = new LiteralNumberProvider(-1f);
        private readonly bool hasManualIndex;

        private int sampleCounter;
        private int cursor;
        private ParticleSnapshot? cursorSnapshot;
        private bool callPending;
        private int callIncrement;

        public InitFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SnapshotRead(parse);
            localSpaceAngles = parse.Boolean("m_bLocalSpaceAngles", localSpaceAngles);
            random = parse.Boolean("m_bRandom", random);
            reverse = parse.Boolean("m_bReverse", reverse);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            resetIndexOnChanges = parse.Boolean("m_bResetSnapshotIndexOnChanges", resetIndexOnChanges);
            increment = parse.CollectionNumberProvider("m_nSnapShotIncrement", increment);
            manualIndex = parse.NumberProvider("m_nManualSnapshotIndex", manualIndex);
            hasManualIndex = manualIndex is not LiteralNumberProvider { Value: -1f };
        }

        public override void Reset()
        {
            sampleCounter = 0;
            cursor = 0;
            cursorSnapshot = null;
        }

        public override ulong WrittenFields => snapshot.AttributeToWrite == ParticleField.Position
            ? FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious)
            : FieldMask(snapshot.AttributeToWrite);

        protected override void BeginCall(Particle first, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            callPending = true;
        }

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

            var isVector = field.FieldType() == "vector";
            var usable = column switch
            {
                Vector3[] => isVector,
                float[] => !isVector && !field.IsIntegerAttribute(),
                int[] => field.IsIntegerAttribute(),
                _ => false,
            };

            if (!usable || rowCount == 0)
            {
                return particle;
            }

            if (callPending)
            {
                callPending = false;
                StartCursor(particleSystemState, rowCount);
            }

            var row = SelectRow(particle, rowCount, isVector, particleSystemState);

            switch (column)
            {
                case Vector3[] vectors when (uint)row < (uint)vectors.Length:
                    WriteVector(particle, vectors[row], particles.PreviousFrameTime, particleSystemState);
                    break;
                case float[] floats when (uint)row < (uint)floats.Length:
                    WriteFloat(particle, floats[row], particleSystemState);
                    break;
                case int[] ints when (uint)row < (uint)ints.Length:
                    particle.SetScalar(field, ints[row]);
                    break;
            }

            return particle;
        }

        private void StartCursor(ParticleSystemState particleSystemState, int rowCount)
        {
            callIncrement = (int)increment.NextNumber(particleSystemState);

            var current = snapshot.Snapshot(particleSystemState);
            if (resetIndexOnChanges && !ReferenceEquals(current, cursorSnapshot))
            {
                cursorSnapshot = current;
                cursor = 0;
            }

            cursor = Math.Min(cursor, rowCount - 1);
        }

        private int SelectRow(Particle particle, int rowCount, bool isVector, ParticleSystemState particleSystemState)
        {
            if (hasManualIndex)
            {
                return Math.Max((int)manualIndex.NextNumber(particle, particleSystemState), 0) % rowCount;
            }

            if (random)
            {
                var sample = particleSystemState.Random.NextOrSeeded(randomSeed, ref sampleCounter);

                return (int)(rowCount * sample);
            }

            var row = reverse ? rowCount - cursor - 1 : cursor;
            var step = isVector ? (2 * callIncrement) - 1 : callIncrement;
            cursor = MathUtils.WrapIndex(cursor + step, rowCount);

            return row;
        }

        /// <summary>
        /// Writes a vector row. A position, previous position or normal moves into the local space
        /// control point's frame, the latter two by its rotation alone, and so do box angles with
        /// <c>m_bLocalSpaceAngles</c>. A previous position row is a velocity, turned into the position
        /// one previous step back from the current one.
        /// </summary>
        private void WriteVector(Particle particle, Vector3 value, float previousStep, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;
            var localSpaceCP = snapshot.LocalSpaceCP;
            var local = localSpaceCP != -1;

            switch (field)
            {
                case ParticleField.PositionPrevious:
                    var dt = previousStep != 0f ? previousStep : 1f / 30f;
                    var step = value * dt;
                    particle.PositionPrevious = particle.Position
                        - (local ? ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCP, step) : step);
                    return;
                case ParticleField.Position:
                    var position = local ? ControlPointTransformProvider.TransformPosition(particleSystemState, localSpaceCP, value) : value;
                    particle.Position = position;
                    particle.PositionPrevious = position;
                    return;
                case ParticleField.Normal when local:
                    value = ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCP, value);
                    break;
                case ParticleField.BoxAngles when local && localSpaceAngles:
                    value = ControlPointTransformProvider.TransformAngles(particleSystemState, localSpaceCP, value);
                    break;
            }

            particle.SetVector(field, value);
        }

        private void WriteFloat(Particle particle, float value, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;

            if (localSpaceAngles && snapshot.LocalSpaceCP != -1 && field.IsOrientationField())
            {
                value = ControlPointTransformProvider.TransformAngle(particleSystemState, snapshot.LocalSpaceCP, field, value);
            }

            particle.SetScalar(field, value);
        }
    }
}

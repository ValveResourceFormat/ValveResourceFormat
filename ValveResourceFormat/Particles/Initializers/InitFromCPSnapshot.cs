using System.Runtime.InteropServices;
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
        private int cursorSerial;

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
            cursorSerial = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        public override ulong WrittenFields => snapshot.AttributeToWrite == ParticleField.Position
            ? ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask()
            : snapshot.AttributeToWrite.Mask();

        protected override void InitializeCall(ParticleSpan run, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;
            var column = snapshot.Column(particleSystemState);
            var rowCount = snapshot.RowCount(particleSystemState);

            var isVector = ParticleBuffer.StorageOf(field) is ParticleFieldStorage.Vector or ParticleFieldStorage.BoneIndices;
            var usable = column switch
            {
                Vector3[] => isVector,
                float[] => !isVector && !field.IsIntegerAttribute(),
                int[] => field.IsIntegerAttribute(),
                _ => false,
            };

            if (!usable || rowCount == 0)
            {
                return;
            }

            var callIncrement = (int)increment.NextNumber(particleSystemState);
            var step = isVector ? (2 * callIncrement) - 1 : callIncrement;

            StartCursor(particleSystemState, rowCount);

            var manualIndices = hasManualIndex ? manualIndex.Fill(run, particleSystemState) : default;
            var rows = MemoryMarshal.Cast<float, int>(ParticleScratch.Floats(run.Length));

            for (var i = 0; i < rows.Length; i++)
            {
                rows[i] = SelectRow(manualIndices, i, rowCount, step, particleSystemState);
            }

            switch (column)
            {
                case Vector3[] vectors:
                    WriteVectors(run, rows, vectors, particleSystemState);
                    break;
                case float[] floats:
                    WriteFloats(run, rows, floats, particleSystemState);
                    break;
                case int[] ints:
                    WriteIntegers(run, rows, ints);
                    break;
            }
        }

        private void StartCursor(ParticleSystemState particleSystemState, int rowCount)
        {
            var current = snapshot.Snapshot(particleSystemState);
            var serial = current?.Serial ?? 0;

            if (resetIndexOnChanges && (!ReferenceEquals(current, cursorSnapshot) || serial != cursorSerial))
            {
                cursorSnapshot = current;
                cursorSerial = serial;
                cursor = 0;
            }

            cursor = Math.Min(cursor, rowCount - 1);
        }

        private int SelectRow(ReadOnlySpan<float> manualIndices, int i, int rowCount, int step, ParticleSystemState particleSystemState)
        {
            if (hasManualIndex)
            {
                return Math.Max((int)manualIndices[i], 0) % rowCount;
            }

            if (random)
            {
                var sample = particleSystemState.Random.NextOrSeeded(randomSeed, ref sampleCounter);

                return (int)(rowCount * sample);
            }

            var row = reverse ? rowCount - cursor - 1 : cursor;
            cursor = MathUtils.WrapIndex(cursor + step, rowCount);

            return row;
        }

        /// <summary>
        /// Writes a vector row. A position, previous position or normal moves into the local space
        /// control point's frame, the latter two by its rotation alone, and so do box angles with
        /// <c>m_bLocalSpaceAngles</c>. A previous position row is a velocity, turned into the position
        /// one previous step back from the current one.
        /// </summary>
        private void WriteVectors(ParticleSpan run, ReadOnlySpan<int> rows, Vector3[] vectors, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;
            var localSpaceCP = snapshot.LocalSpaceCP;
            var local = localSpaceCP != -1;

            if (field == ParticleField.PositionPrevious)
            {
                var previousStep = particleSystemState.PreviousStep;
                var dt = previousStep != 0f ? previousStep : 1f / 30f;
                var currentPositions = run.ReadVectors(ParticleField.Position);
                var previousPositions = run.Vectors(ParticleField.PositionPrevious);

                for (var i = 0; i < rows.Length; i++)
                {
                    if ((uint)rows[i] >= (uint)vectors.Length)
                    {
                        continue;
                    }

                    var step = vectors[rows[i]] * dt;
                    previousPositions[i] = currentPositions[i]
                        - (local ? ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCP, step) : step);
                }

                return;
            }

            if (field == ParticleField.Position)
            {
                var positions = run.Vectors(ParticleField.Position);
                var previousPositions = run.Vectors(ParticleField.PositionPrevious);

                for (var i = 0; i < rows.Length; i++)
                {
                    if ((uint)rows[i] >= (uint)vectors.Length)
                    {
                        continue;
                    }

                    var value = vectors[rows[i]];
                    var position = local ? ControlPointTransformProvider.TransformPosition(particleSystemState, localSpaceCP, value) : value;
                    positions[i] = position;
                    previousPositions[i] = position;
                }

                return;
            }

            if (!ParticleBuffer.IsVector(field))
            {
                return;
            }

            var outputs = run.Vectors(field);

            for (var i = 0; i < rows.Length; i++)
            {
                if ((uint)rows[i] >= (uint)vectors.Length)
                {
                    continue;
                }

                var value = vectors[rows[i]];

                if (field == ParticleField.Normal && local)
                {
                    value = ControlPointTransformProvider.TransformDirection(particleSystemState, localSpaceCP, value);
                }
                else if (field == ParticleField.BoxAngles && local && localSpaceAngles)
                {
                    value = ControlPointTransformProvider.TransformAngles(particleSystemState, localSpaceCP, value);
                }

                outputs[i] = value;
            }
        }

        private void WriteFloats(ParticleSpan run, ReadOnlySpan<int> rows, float[] floats, ParticleSystemState particleSystemState)
        {
            var field = snapshot.AttributeToWrite;

            if (!ParticleBuffer.IsScalar(field))
            {
                return;
            }

            var outputs = run.Floats(field);
            var transformAngles = localSpaceAngles && snapshot.LocalSpaceCP != -1 && field.IsOrientationField();

            for (var i = 0; i < rows.Length; i++)
            {
                if ((uint)rows[i] >= (uint)floats.Length)
                {
                    continue;
                }

                var value = floats[rows[i]];

                if (transformAngles)
                {
                    value = ControlPointTransformProvider.TransformAngle(particleSystemState, snapshot.LocalSpaceCP, field, value);
                }

                outputs[i] = value;
            }
        }

        private void WriteIntegers(ParticleSpan run, ReadOnlySpan<int> rows, int[] ints)
        {
            var outputs = run.Integers(snapshot.AttributeToWrite);

            for (var i = 0; i < rows.Length; i++)
            {
                if ((uint)rows[i] < (uint)ints.Length)
                {
                    outputs[i] = ints[rows[i]];
                }
            }
        }
    }
}

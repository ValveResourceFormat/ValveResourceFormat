using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Operators
{
    /// <summary>
    /// Pulls every particle each step toward a point of a skinned control point snapshot, deformed
    /// by the current pose of the bones of another control point's model, picked by index, by a
    /// walk from a start point that begins again every step, or at random every step. Both the
    /// position and the previous position move, so a full pull leaves particles still.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_MovementSkinnedPositionFromCPSnapshot">C_OP_MovementSkinnedPositionFromCPSnapshot</seealso>
    class MovementSkinnedPositionFromCPSnapshot : ParticleFunctionOperator
    {
        private readonly SkinnedSnapshot snapshot;
        private readonly int controlPoint;
        private readonly bool random;
        private readonly int randomSeed;
        private readonly bool setNormal;
        private readonly bool setRadius;
        private readonly SnapshotIndexType indexType = SnapshotIndexType.SNAPSHOT_INDEX_INCREMENT;
        private readonly INumberProvider readIndex = new LiteralNumberProvider(0f);
        private readonly INumberProvider increment = new LiteralNumberProvider(1f);
        private readonly INumberProvider fullLoopIncrement = new LiteralNumberProvider(0f);
        private readonly INumberProvider startPoint = new LiteralNumberProvider(0f);
        private readonly INumberProvider interpolation = new LiteralNumberProvider(1f);
        private readonly ulong storedFields;

        private int randomCounter;

        public MovementSkinnedPositionFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SkinnedSnapshot(parse);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            random = parse.Boolean("m_bRandom", random);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            setRadius = parse.Boolean("m_bSetRadius", setRadius);
            indexType = parse.Enum("m_nIndexType", indexType);
            readIndex = parse.NumberProvider("m_flReadIndex", readIndex);
            increment = parse.CollectionNumberProvider("m_flIncrement", increment);
            fullLoopIncrement = parse.CollectionNumberProvider("m_nFullLoopIncrement", fullLoopIncrement);
            startPoint = parse.CollectionNumberProvider("m_nSnapShotStartPoint", startPoint);
            interpolation = parse.NumberProvider("m_flInterpolation", interpolation);

            storedFields = ParticleField.Position.Mask() | ParticleField.PositionPrevious.Mask() | ParticleField.HitboxOffsetPosition.Mask()
                | ParticleField.BoneIndices.Mask() | ParticleField.BoneWeights.Mask();

            if (setNormal)
            {
                storedFields |= ParticleField.Normal.Mask();
            }

            if (setRadius)
            {
                storedFields |= ParticleField.Radius.Mask();
            }
        }

        public override void Reset()
        {
            randomCounter = 0;
        }

        public override void Restart(ParticleSystemState particleSystemState) => Reset();

        /// <inheritdoc/>
        public override ulong StoredFields => storedFields;

        public override void Operate(ParticleCollection particles, float frameTime, ParticleSystemState particleSystemState, float strength)
        {
            var step = (int)increment.NextNumber(particleSystemState);
            var loop = (int)fullLoopIncrement.NextNumber(particleSystemState);
            var next = (int)startPoint.NextNumber(particleSystemState);

            var cache = HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true);

            if (snapshot.Resolve(particleSystemState) is not { } columns
                || (setNormal && columns.Normals == null)
                || (setRadius && columns.Radii == null))
            {
                return;
            }

            var count = columns.Count;
            var model = cache is { Count: > 0 } ? cache.Object?.Model : null;

            var current = particles.Current;
            var positions = current.Vectors(ParticleField.Position);
            var previousPositions = current.Vectors(ParticleField.PositionPrevious);
            var normals = setNormal ? current.Vectors(ParticleField.Normal) : default;
            var radii = setRadius ? current.Floats(ParticleField.Radius) : default;
            var boneIndices = current.BoneIndices;
            var boneWeights = current.Vectors(ParticleField.BoneWeights);
            var hitboxOffsets = current.Vectors(ParticleField.HitboxOffsetPosition);

            var weights = interpolation.Fill(current, particleSystemState);

            var readIndices = default(Span<float>);

            if (indexType == SnapshotIndexType.SNAPSHOT_INDEX_DIRECT)
            {
                readIndices = readIndex.Fill(current, particleSystemState);
            }

            for (var i = 0; i < positions.Length; i++)
            {
                var weight = weights[i];
                int row;

                if (indexType == SnapshotIndexType.SNAPSHOT_INDEX_DIRECT)
                {
                    row = (int)readIndices[i] % count;
                }
                else if (random)
                {
                    row = SkinnedSnapshot.RandomRow(particleSystemState, count, randomSeed, ref randomCounter);
                }
                else
                {
                    row = SkinnedSnapshot.WalkRow(next, count, loop);
                    next = row + step;
                }

                if ((uint)row >= (uint)count)
                {
                    continue;
                }

                var target = columns.Positions[row];
                var normal = setNormal ? columns.Normals![row] : Vector3.Zero;
                var radius = setRadius ? columns.Radii![row] : 0f;

                if (model != null)
                {
                    boneIndices[i] = columns.BoneIndices(row, model, cache!.Count);
                    hitboxOffsets[i] = columns.Positions[row];
                    boneWeights[i] = columns.BoneWeights(row);

                    cache.ComputeSkinningMatrix(boneIndices[i], boneWeights[i], false, out var skin, out _);
                    target = Vector3.Transform(target, skin);
                    normal = MathUtils.SafeNormalize(Vector3.TransformNormal(normal, skin));
                    radius *= skin.MaxAxisScale();
                }

                positions[i] = MathUtils.Lerp(positions[i], target, weight);
                previousPositions[i] = MathUtils.Lerp(previousPositions[i], target, weight);

                if (setNormal)
                {
                    normals[i] = MathUtils.Lerp(normals[i], normal, weight);
                }

                if (setRadius)
                {
                    radii[i] = MathUtils.Lerp(radii[i], radius, weight);
                }
            }
        }
    }
}

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
        private enum SnapshotIndexType
        {
            SNAPSHOT_INDEX_INCREMENT = 0,
            SNAPSHOT_INDEX_DIRECT = 1,
        }

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

        private int randomCounter;

        public MovementSkinnedPositionFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SkinnedSnapshot(parse);
            controlPoint = Math.Clamp(parse.Int32("m_nControlPointNumber", controlPoint), 0, ParticleSystemState.MaxControlPoint);
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
        }

        public override void Reset()
        {
            randomCounter = 0;
        }

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

            foreach (ref var particle in particles.Current)
            {
                var weight = interpolation.NextNumber(ref particle, particleSystemState);
                int row;

                if (indexType == SnapshotIndexType.SNAPSHOT_INDEX_DIRECT)
                {
                    row = (int)readIndex.NextNumber(ref particle, particleSystemState) % count;
                }
                else if (random)
                {
                    var sample = randomSeed != 0
                        ? ParticleRandom.ForSample(particleSystemState.Random.Seed + randomSeed + randomCounter++)
                        : particleSystemState.Random.Next();
                    row = (int)(count * sample);
                }
                else
                {
                    if (next >= count)
                    {
                        next = (next + (loop * (next / count))) % count;
                    }

                    row = next;
                    next += step;
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
                    var skinning = columns.Skinning[row];
                    particle.BoneIndices = SkinnedSnapshot.BoneIndices(skinning, model, cache!.Count);
                    particle.HitboxOffsetPosition = target;
                    particle.BoneWeights = SkinnedSnapshot.BoneWeights(skinning);

                    cache.ComputeSkinningMatrix(particle.BoneIndices, particle.BoneWeights, false, out var skin, out _);
                    target = Vector3.Transform(target, skin);
                    normal = MathUtils.SafeNormalize(Vector3.TransformNormal(normal, skin));
                    radius *= skin.MaxAxisScale();
                }

                particle.Position += (target - particle.Position) * weight;
                particle.PositionPrevious += (target - particle.PositionPrevious) * weight;

                if (setNormal)
                {
                    particle.Normal += (normal - particle.Normal) * weight;
                }

                if (setRadius)
                {
                    particle.Radius += (radius - particle.Radius) * weight;
                }
            }
        }
    }
}

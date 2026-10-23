using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.Initializers
{
    /// <summary>
    /// Places new particles on the points of a skinned control point snapshot, deformed by the
    /// current pose of the bones of another control point's model, and records the rest position
    /// and bones for the operators that keep them skinned. Without bones the snapshot points are
    /// taken as they are. Normals, radii, colour and alpha can be taken along, and particles can
    /// leave along their normal or with the velocity of their bones.
    /// </summary>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_INIT_InitSkinnedPositionFromCPSnapshot">C_INIT_InitSkinnedPositionFromCPSnapshot</seealso>
    class InitSkinnedPositionFromCPSnapshot : ParticleFunctionInitializer
    {
        private readonly SkinnedSnapshot snapshot;
        private readonly int controlPoint;
        private readonly bool random;
        private readonly int randomSeed;
        private readonly bool rigid;
        private readonly bool setNormal;
        private readonly bool ignoreDt;
        private readonly float minNormalVelocity;
        private readonly float maxNormalVelocity;
        private readonly SnapshotIndexType indexType = SnapshotIndexType.SNAPSHOT_INDEX_INCREMENT;
        private readonly INumberProvider readIndex = new LiteralNumberProvider(0f);
        private readonly float increment = 1f;
        private readonly int fullLoopIncrement;
        private readonly int startPoint;
        private readonly float boneVelocity;
        private readonly float boneVelocityMax;
        private readonly bool copyColor;
        private readonly bool copyAlpha;
        private readonly bool setRadius;

        private int nextIndex;
        private int randomCounter;

        public InitSkinnedPositionFromCPSnapshot(ParticleDefinitionParser parse) : base(parse)
        {
            snapshot = new SkinnedSnapshot(parse);
            controlPoint = parse.ControlPoint("m_nControlPointNumber", controlPoint);
            random = parse.Boolean("m_bRandom", random);
            randomSeed = parse.Int32("m_nRandomSeed", randomSeed);
            rigid = parse.Boolean("m_bRigid", rigid);
            setNormal = parse.Boolean("m_bSetNormal", setNormal);
            ignoreDt = parse.Boolean("m_bIgnoreDt", ignoreDt);
            minNormalVelocity = parse.Float("m_flMinNormalVelocity", minNormalVelocity);
            maxNormalVelocity = parse.Float("m_flMaxNormalVelocity", maxNormalVelocity);
            indexType = parse.Enum("m_nIndexType", indexType);
            readIndex = parse.NumberProvider("m_flReadIndex", readIndex);
            increment = parse.Float("m_flIncrement", increment);
            fullLoopIncrement = parse.Int32("m_nFullLoopIncrement", fullLoopIncrement);
            startPoint = parse.Int32("m_nSnapShotStartPoint", startPoint);
            boneVelocity = parse.Float("m_flBoneVelocity", boneVelocity);
            boneVelocityMax = parse.Float("m_flBoneVelocityMax", boneVelocityMax);
            copyColor = parse.Boolean("m_bCopyColor", copyColor);
            copyAlpha = parse.Boolean("m_bCopyAlpha", copyAlpha);
            setRadius = parse.Boolean("m_bSetRadius", setRadius);
        }

        public override void Reset()
        {
            nextIndex = startPoint;
            randomCounter = 0;
        }

        public override ulong WrittenFields
        {
            get
            {
                var fields = FieldMask(ParticleField.Position) | FieldMask(ParticleField.PositionPrevious) | FieldMask(ParticleField.HitboxOffsetPosition)
                    | FieldMask(ParticleField.BoneIndices) | FieldMask(ParticleField.BoneWeights);

                fields |= setNormal ? FieldMask(ParticleField.Normal) : 0;
                fields |= copyColor ? FieldMask(ParticleField.Color) : 0;
                fields |= copyAlpha ? FieldMask(ParticleField.Alpha) : 0;
                fields |= setRadius ? FieldMask(ParticleField.Radius) : 0;
                return fields;
            }
        }

        public override Particle Initialize(Particle particle, ParticleCollection particles, ParticleSystemState particleSystemState)
        {
            if (snapshot.Resolve(particleSystemState) is not { } columns
                || (setNormal && columns.Normals == null)
                || (copyColor && columns.Colors == null)
                || (copyAlpha && columns.Alphas == null)
                || (setRadius && columns.Radii == null))
            {
                return particle;
            }

            var row = SelectRow(particle, particleSystemState, columns.Count);

            if ((uint)row >= (uint)columns.Count)
            {
                return particle;
            }

            if (copyColor)
            {
                particle.Color = columns.Colors![row];
            }

            if (copyAlpha)
            {
                particle.Alpha = columns.Alphas![row];
            }

            var local = columns.Positions[row];
            var cache = HitBoxCache.Refresh(particleSystemState, controlPoint, string.Empty, useBones: true);

            if (cache is not { Count: > 0, Object: { } modelObject })
            {
                particle.Position = local;
                particle.PositionPrevious = local;

                if (setNormal)
                {
                    particle.Normal = columns.Normals![row];
                }

                if (setRadius)
                {
                    particle.Radius = columns.Radii![row];
                }

                return particle;
            }

            columns.Bind(particle, row, modelObject.Model, cache.Count);

            var withBoneVelocity = boneVelocity != 0f && cache.PreviousUpdateTime != -1f;
            cache.ComputeSkinningMatrix(particle.BoneIndices, particle.BoneWeights, withBoneVelocity, out var skin, out var previousSkin);

            var normalOffset = Vector3.Zero;

            if (setNormal)
            {
                var normal = columns.Normals![row];

                if (!rigid)
                {
                    normal = MathUtils.SafeNormalize(Vector3.TransformNormal(normal, skin));
                }

                particle.Normal = normal;

                var speed = particleSystemState.Random.NextBetween(minNormalVelocity, maxNormalVelocity);
                normalOffset = normal * (ignoreDt ? speed : speed * particles.PreviousFrameTime);
            }

            if (setRadius)
            {
                particle.Radius = columns.Radii![row] * (rigid ? 1f : skin.MaxAxisScale());
            }

            if (rigid)
            {
                particle.Position = Vector3.Zero;
                particle.PositionPrevious = Vector3.Zero;
            }
            else
            {
                particle.Position = Vector3.Transform(local, skin);
                particle.PositionPrevious = Vector3.Transform(local + normalOffset, skin);
            }

            var interval = cache.UpdateTime - cache.PreviousUpdateTime;

            if (withBoneVelocity && interval > 0f && float.IsFinite(previousSkin.M41 + previousSkin.M42 + previousSkin.M43))
            {
                var velocity = (skin.Translation - previousSkin.Translation) / interval * boneVelocity;

                if (boneVelocityMax > 0f)
                {
                    velocity = MathUtils.ClampLength(velocity, boneVelocityMax);
                }

                particle.PositionPrevious -= ignoreDt ? velocity : velocity * particles.PreviousFrameTime;
            }

            return particle;
        }

        private int SelectRow(Particle particle, ParticleSystemState particleSystemState, int count)
        {
            if (indexType == SnapshotIndexType.SNAPSHOT_INDEX_DIRECT)
            {
                return (int)readIndex.NextNumber(particle, particleSystemState) % count;
            }

            if (random)
            {
                return SkinnedSnapshot.RandomRow(particleSystemState, count, randomSeed, ref randomCounter);
            }

            var index = SkinnedSnapshot.WalkRow(nextIndex, count, fullLoopIncrement);
            nextIndex = index + (int)increment;
            return index;
        }
    }
}

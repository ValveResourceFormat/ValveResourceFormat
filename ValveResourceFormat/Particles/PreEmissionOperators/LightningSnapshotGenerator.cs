using System.Collections;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Builds a branching lightning bolt between two control points and publishes it as a snapshot on
    /// a control point, for snapshot emitters and initializers to spawn one particle per bolt row.
    /// </summary>
    /// <remarks>
    /// The bolt is one flat list of rows. Each pass inserts a jittered midpoint between every pair of
    /// connected neighbours, and a midpoint may split off a branch, which is appended to the end of
    /// the list and subdivided by the passes that follow. Every run of connected rows is closed by
    /// separator rows with zero radius and an <see cref="ParticleField.AlphaAlternate"/> of 0, so a
    /// rope drawn through all of them in order draws the joins between runs at zero width.
    ///
    /// <para>The snapshot carries position, radius, the connected flag as alpha2 and a parametric
    /// texture coordinate as scratch float. It is padded to the row count of the first bolt (or
    /// <c>m_flDedicatedPool</c>) with separator rows at the end point, and capped at the system's
    /// particle limit.</para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LightningSnapshotGenerator">C_OP_LightningSnapshotGenerator</seealso>
    class LightningSnapshotGenerator : ParticleFunctionPreEmissionOperator
    {
        private const float MaxSegments = 8f;

        private readonly int snapshotControlPoint;
        private readonly int startControlPoint;
        private readonly int endControlPoint = 1;

        private readonly INumberProvider segments = new LiteralNumberProvider(4f);
        private readonly INumberProvider offset = new LiteralNumberProvider(32f);
        private readonly INumberProvider offsetDecay = new LiteralNumberProvider(0.5f);
        private readonly INumberProvider recalcRate = new LiteralNumberProvider(-1f);
        private readonly INumberProvider uvScale = new LiteralNumberProvider(100f);
        private readonly INumberProvider uvOffset = new LiteralNumberProvider(0f);
        private readonly INumberProvider splitRate = new LiteralNumberProvider(0.25f);
        private readonly INumberProvider recursionSplitScale = new LiteralNumberProvider(1f);
        private readonly bool scaleBranchDistance;
        private readonly INumberProvider branchDistanceScale = new LiteralNumberProvider(1f);
        private readonly bool scaleBranchOffset;
        private readonly INumberProvider branchOffsetScale = new LiteralNumberProvider(1f);
        private readonly INumberProvider branchTwist = new LiteralNumberProvider(0.5f);
        private readonly ParticleLightningBranchBehavior branchBehavior;
        private readonly INumberProvider radiusStart = new LiteralNumberProvider(10f);
        private readonly INumberProvider radiusEnd = new LiteralNumberProvider(0f);
        private readonly INumberProvider dedicatedPool = new LiteralNumberProvider(-1f);

        private float nextGenerationTime;
        private int poolSize = -1;

        public LightningSnapshotGenerator(ParticleDefinitionParser parse) : base(parse)
        {
            snapshotControlPoint = parse.Int32("m_nCPSnapshot", snapshotControlPoint);
            startControlPoint = parse.Int32("m_nCPStartPnt", startControlPoint);
            endControlPoint = parse.Int32("m_nCPEndPnt", endControlPoint);
            segments = parse.NumberProvider("m_flSegments", segments);
            offset = parse.NumberProvider("m_flOffset", offset);
            offsetDecay = parse.NumberProvider("m_flOffsetDecay", offsetDecay);
            recalcRate = parse.NumberProvider("m_flRecalcRate", recalcRate);
            uvScale = parse.NumberProvider("m_flUVScale", uvScale);
            uvOffset = parse.NumberProvider("m_flUVOffset", uvOffset);
            splitRate = parse.NumberProvider("m_flSplitRate", splitRate);
            recursionSplitScale = parse.NumberProvider("m_flRecursionSplitScale", recursionSplitScale);
            scaleBranchDistance = parse.Boolean("m_bScaleBranchDistance", scaleBranchDistance);
            branchDistanceScale = parse.NumberProvider("m_flBranchDistanceScale", branchDistanceScale);
            scaleBranchOffset = parse.Boolean("m_bScaleBranchOffset", scaleBranchOffset);
            branchOffsetScale = parse.NumberProvider("m_flBranchOffsetScale", branchOffsetScale);
            branchTwist = parse.NumberProvider("m_flBranchTwist", branchTwist);
            branchBehavior = parse.Enum("m_nBranchBehavior", branchBehavior);
            radiusStart = parse.NumberProvider("m_flRadiusStart", radiusStart);
            radiusEnd = parse.NumberProvider("m_flRadiusEnd", radiusEnd);
            dedicatedPool = parse.NumberProvider("m_flDedicatedPool", dedicatedPool);
        }

        public override void Reset()
        {
            nextGenerationTime = 0f;
            poolSize = -1;
        }

        /// <summary>
        /// Builds a new bolt once the system's age passes the next generation time. <c>m_flRecalcRate</c>
        /// is the period until the next one, so 0 rebuilds every step and a negative rate keeps the first.
        /// </summary>
        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            if (particleSystemState.Age <= nextGenerationTime)
            {
                return;
            }

            var period = recalcRate.NextNumber(particleSystemState);
            nextGenerationTime = period >= 0f ? particleSystemState.Age + period : float.MaxValue;

            var passes = segments.NextNumber(particleSystemState);
            passes = passes >= 0f ? MathF.Min(MaxSegments, passes) : 0f;

            var textureStart = uvOffset.NextNumber(particleSystemState);

            var builder = new BoltBuilder(particleSystemState.Random)
            {
                Passes = passes,
                Offset = offset.NextNumber(particleSystemState),
                OffsetDecay = offsetDecay.NextNumber(particleSystemState),
                SplitRate = Math.Clamp(splitRate.NextNumber(particleSystemState), 0f, 0.25f),
                RecursionSplitScale = Math.Clamp(recursionSplitScale.NextNumber(particleSystemState), 0f, 10f),
                ScaleBranchDistance = scaleBranchDistance,
                BranchDistanceScale = Math.Clamp(branchDistanceScale.NextNumber(particleSystemState), 1e-4f, 100f),
                ScaleBranchOffset = scaleBranchOffset,
                BranchOffsetScale = Math.Clamp(branchOffsetScale.NextNumber(particleSystemState), 0f, 100f),
                BranchTwist = branchTwist.NextNumber(particleSystemState),
                BranchBehavior = branchBehavior,
                TextureEnd = textureStart + uvScale.NextNumber(particleSystemState),
                End = particleSystemState.GetControlPoint(endControlPoint).Position,
            };

            builder.Build(particleSystemState.GetControlPoint(startControlPoint).Position, textureStart,
                radiusStart.NextNumber(particleSystemState), radiusEnd.NextNumber(particleSystemState));

            if (poolSize < 0)
            {
                var dedicated = (int)dedicatedPool.NextNumber(particleSystemState);
                poolSize = dedicated > 0 ? dedicated : builder.RowCount;
            }

            builder.PadTo(poolSize);

            var capacity = particleSystemState.Data?.ParticleCapacity ?? builder.RowCount;
            var snapshot = builder.ToSnapshot(Math.Min(builder.RowCount, capacity));
            particleSystemState.Data?.SetControlPointSnapshot(snapshotControlPoint, snapshot);
        }

        /// <summary>
        /// One row of the bolt. <see cref="Generation"/> is 1 on the main bolt and one more on each
        /// branch level, and -1 on separator rows.
        /// </summary>
        private readonly record struct BoltRow(Vector3 Position, float Radius, bool Connected, float TextureCoordinate, int Generation)
        {
            public static BoltRow Separator(Vector3 position, float textureCoordinate) => new(position, 0f, false, textureCoordinate, -1);
        }

        private sealed class BoltBuilder(ParticleRandom random)
        {
            private readonly List<BoltRow> rows = [];

            public float Passes { get; init; }
            public float Offset { get; init; }
            public float OffsetDecay { get; init; }
            public float SplitRate { get; init; }
            public float RecursionSplitScale { get; init; }
            public bool ScaleBranchDistance { get; init; }
            public float BranchDistanceScale { get; init; }
            public bool ScaleBranchOffset { get; init; }
            public float BranchOffsetScale { get; init; }
            public float BranchTwist { get; init; }
            public ParticleLightningBranchBehavior BranchBehavior { get; init; }
            public float TextureEnd { get; init; }
            public Vector3 End { get; init; }

            public int RowCount => rows.Count;

            public void Build(Vector3 start, float textureStart, float startRadius, float endRadius)
            {
                rows.Add(new BoltRow(start, startRadius, true, textureStart, 1));
                rows.Add(new BoltRow(End, endRadius, true, TextureEnd, 1));
                rows.Add(BoltRow.Separator(End, TextureEnd));
                rows.Add(BoltRow.Separator(End, TextureEnd));

                var offset = Offset;

                for (var pass = 0; Passes > pass; pass++)
                {
                    for (var i = 0; i + 1 < rows.Count; i++)
                    {
                        if (!rows[i].Connected || !rows[i + 1].Connected)
                        {
                            continue;
                        }

                        Subdivide(i, offset);
                        i++;
                    }

                    offset *= OffsetDecay;
                }
            }

            /// <summary>
            /// Inserts a midpoint after row <paramref name="index"/>, pushed off the span by a random
            /// point in a ball of half the jitter and by up to half the jitter along the span's cross
            /// product with the row's position, then rolls for a branch splitting off it.
            /// </summary>
            private void Subdivide(int index, float offset)
            {
                var from = rows[index];
                var to = rows[index + 1];
                var generation = from.Generation;

                var jitter = ScaleBranchDistance ? offset / (BranchDistanceScale * generation) : offset;

                var direction = MathUtils.SafeNormalize(to.Position - from.Position, Vector3.UnitZ);
                // Intentional: crossing with the row's world position makes the jitter depend on where the bolt is
                var sideways = MathUtils.SafeNormalize(Vector3.Cross(direction, from.Position));
                var wander = random.NextInUnitBall(out _);
                var push = random.NextBetween(-jitter, jitter) / generation;

                var midpoint = ((from.Position + to.Position) * 0.5f) + (wander * (jitter * 0.5f)) + (sideways * (push * 0.5f));

                rows.Insert(index + 1, new BoltRow(midpoint, (from.Radius + to.Radius) * 0.5f, true,
                    (from.TextureCoordinate + to.TextureCoordinate) * 0.5f, generation));

                var splitScale = RecursionSplitScale == 1f ? 1f : (1f - (generation / Passes)) * RecursionSplitScale;

                if (SplitRate * splitScale / generation > random.Next())
                {
                    AddBranch(index, jitter);
                }
            }

            /// <summary>
            /// Appends a branch from the midpoint just inserted after row <paramref name="index"/>,
            /// framed by separator rows, ending at zero radius halfway from the midpoint's texture
            /// coordinate to the bolt's last one.
            /// </summary>
            private void AddBranch(int index, float jitter)
            {
                var origin = rows[index + 1];
                var generation = origin.Generation + 1;
                var offsetScale = ScaleBranchOffset ? generation * BranchOffsetScale : 1f;

                var wander = random.NextInUnitBall(out _);
                var length = ((Vector3.Distance(origin.Position, End) * 0.1875f) + (Vector3.Distance(rows[0].Position, rows[1].Position) * 0.75f)) * 0.5f;
                var direction = MathUtils.SafeNormalize(origin.Position - rows[index].Position);
                var push = random.NextBetween(-jitter, jitter);

                var branchEnd = BranchBehavior == ParticleLightningBranchBehavior.PARTICLE_LIGHTNING_BRANCH_ENDPOINT_DIR
                    ? origin.Position + (Vector3.Lerp(MathUtils.SafeNormalize(End - origin.Position), direction, BranchTwist) * length) + (wander * (push * offsetScale))
                    : origin.Position + (direction * length) + (wander * (BranchTwist * push * offsetScale));

                var textureEnd = ((TextureEnd - origin.TextureCoordinate) * 0.5f) + origin.TextureCoordinate;

                rows.Add(BoltRow.Separator(origin.Position, origin.TextureCoordinate));
                rows.Add(BoltRow.Separator(origin.Position, origin.TextureCoordinate));
                rows.Add(origin with { Generation = generation });
                rows.Add(new BoltRow(branchEnd, 0f, true, textureEnd, generation));
                rows.Add(BoltRow.Separator(branchEnd, textureEnd));
                rows.Add(BoltRow.Separator(branchEnd, textureEnd));
            }

            public void PadTo(int count)
            {
                while (rows.Count < count)
                {
                    rows.Add(BoltRow.Separator(End, 0f));
                }
            }

            public ParticleSnapshot ToSnapshot(int count)
            {
                var positions = new Vector3[count];
                var radii = new float[count];
                var connected = new float[count];
                var textureCoordinates = new float[count];

                for (var i = 0; i < count; i++)
                {
                    var row = rows[i];
                    positions[i] = row.Position;
                    radii[i] = row.Radius;
                    connected[i] = row.Connected ? 1f : 0f;
                    textureCoordinates[i] = row.TextureCoordinate;
                }

                var columns = new Dictionary<(string Name, string Type), IEnumerable>
                {
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Position)!, "float3")] = positions,
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Radius)!, "float")] = radii,
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.AlphaAlternate)!, "float")] = connected,
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.ScratchFloat)!, "float")] = textureCoordinates,
                };

                return ParticleSnapshot.Create((uint)count, columns);
            }
        }
    }
}

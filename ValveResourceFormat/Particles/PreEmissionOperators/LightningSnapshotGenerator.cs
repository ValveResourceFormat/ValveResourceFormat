using System.Collections;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Particles.Utils;

namespace ValveResourceFormat.Particles.PreEmissionOperators
{
    /// <summary>
    /// Builds a branching lightning bolt between two control points and publishes it as a snapshot on
    /// a control point, for snapshot emitters and initializers to spawn one particle per bolt point.
    /// </summary>
    /// <remarks>
    /// The bolt is a midpoint displacement: the span is halved <c>m_flSegments</c> times, each new
    /// midpoint pushed sideways by up to the current offset, which shrinks by <c>m_flOffsetDecay</c>
    /// per level. A new midpoint may split off a branch reaching as far as the rest of the span it
    /// halves, itself a bolt with the recursion levels and offset left at that depth, so branches
    /// split early are long and coarse and branches split late are short. The split chance, branch
    /// reach and branch offset and twist are each scaled per recursion level when authored to.
    ///
    /// <para>Every bolt and branch writes its points as one contiguous run tagged with its own
    /// <see cref="ParticleField.RopeSegmentId"/>, so a rope or cable drawing the particles can keep
    /// the branches apart instead of threading one strip through all of them.</para>
    /// </remarks>
    /// <seealso href="https://s2v.app/SchemaExplorer/cs2/particles/C_OP_LightningSnapshotGenerator">C_OP_LightningSnapshotGenerator</seealso>
    class LightningSnapshotGenerator : ParticleFunctionPreEmissionOperator
    {
        /// <summary>Deepest midpoint recursion honoured, 1025 points per bolt.</summary>
        public const int MaxRecursionDepth = 10;

        /// <summary>Deepest branch nesting honoured, so a high split rate cannot recurse without end.</summary>
        public const int MaxBranchLevel = 4;

        /// <summary>Row ceiling when no dedicated pool is authored, matching the particle pool ceiling.</summary>
        public const int DefaultMaxRows = 20000;

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

        private bool generated;
        private float timeSinceGenerated;

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
            generated = false;
            timeSinceGenerated = 0f;
        }

        public override void Operate(ref ParticleSystemState particleSystemState, float frameTime)
        {
            // A non-positive rate keeps the first bolt for the life of the system
            if (generated)
            {
                var rate = recalcRate.NextNumber(particleSystemState);
                timeSinceGenerated += frameTime;

                if (rate <= 0f || timeSinceGenerated < 1f / rate)
                {
                    return;
                }
            }

            generated = true;
            timeSinceGenerated = 0f;

            var pool = dedicatedPool.NextNumber(particleSystemState);

            var settings = new LightningSettings
            {
                Depth = Math.Clamp((int)MathF.Round(segments.NextNumber(particleSystemState)), 0, MaxRecursionDepth),
                Offset = offset.NextNumber(particleSystemState),
                OffsetDecay = offsetDecay.NextNumber(particleSystemState),
                UvScale = uvScale.NextNumber(particleSystemState),
                UvOffset = uvOffset.NextNumber(particleSystemState),
                SplitRate = splitRate.NextNumber(particleSystemState),
                RecursionSplitScale = recursionSplitScale.NextNumber(particleSystemState),
                BranchDistanceScale = scaleBranchDistance ? branchDistanceScale.NextNumber(particleSystemState) : 1f,
                BranchOffsetScale = scaleBranchOffset ? branchOffsetScale.NextNumber(particleSystemState) : 1f,
                BranchTwist = branchTwist.NextNumber(particleSystemState),
                BranchBehavior = branchBehavior,
                RadiusStart = radiusStart.NextNumber(particleSystemState),
                RadiusEnd = radiusEnd.NextNumber(particleSystemState),
                MaxRows = pool > 0f ? Math.Min((int)pool, DefaultMaxRows) : DefaultMaxRows,
            };

            var start = particleSystemState.GetControlPoint(startControlPoint).Position;
            var end = particleSystemState.GetControlPoint(endControlPoint).Position;

            var snapshot = Build(start, end, settings, particleSystemState.Random.Next);
            particleSystemState.Data?.SetControlPointSnapshot(snapshotControlPoint, snapshot);
        }

        /// <summary>The evaluated inputs of one bolt generation.</summary>
        internal readonly record struct LightningSettings
        {
            public int Depth { get; init; }
            public float Offset { get; init; }
            public float OffsetDecay { get; init; }
            public float UvScale { get; init; }
            public float UvOffset { get; init; }
            public float SplitRate { get; init; }
            public float RecursionSplitScale { get; init; }
            public float BranchDistanceScale { get; init; }
            public float BranchOffsetScale { get; init; }
            public float BranchTwist { get; init; }
            public ParticleLightningBranchBehavior BranchBehavior { get; init; }
            public float RadiusStart { get; init; }
            public float RadiusEnd { get; init; }
            public int MaxRows { get; init; }
        }

        /// <summary>
        /// Builds the bolt from <paramref name="start"/> to <paramref name="end"/>. Every random draw
        /// goes through <paramref name="random"/> in a fixed order, so the same sequence always gives
        /// the same bolt. A bolt with coincident end points has nowhere to go and yields no rows.
        /// </summary>
        internal static ParticleSnapshot Build(Vector3 start, Vector3 end, in LightningSettings settings, Func<float> random)
        {
            var builder = new BoltBuilder(settings, random);

            if (Vector3.DistanceSquared(start, end) > ParticleMath.MinimumLengthSquared)
            {
                builder.AddBolt(start, end, settings.Depth, settings.Offset, settings.RadiusStart, settings.RadiusEnd, firstLevel: 0, branchLevel: 0);
            }

            return builder.ToSnapshot();
        }

        private sealed class BoltBuilder(LightningSettings settings, Func<float> random)
        {
            private readonly List<Vector3> positions = [];
            private readonly List<float> radii = [];
            private readonly List<float> textureCoordinates = [];
            private readonly List<int> segmentIds = [];
            private int nextSegmentId;

            private bool IsFull => positions.Count >= settings.MaxRows;

            public void AddBolt(Vector3 start, Vector3 end, int depth, float offset, float startRadius, float endRadius, int firstLevel, int branchLevel)
            {
                // A run needs two rows to draw as a segment, a lone leftover row would only be a dot
                if (settings.MaxRows - positions.Count < 2)
                {
                    return;
                }

                var axis = MathUtils.SafeNormalize(end - start, Vector3.UnitX, ParticleMath.MinimumLengthSquared);

                var count = (1 << depth) + 1;
                var points = new Vector3[count];
                points[0] = start;
                points[^1] = end;

                var branches = new List<Branch>();
                Displace(points, end, axis, offset, firstLevel, branchLevel, branches);

                var segmentId = nextSegmentId++;
                var arcLength = 0f;
                var oneOverUvScale = MathF.Abs(settings.UvScale) > float.Epsilon ? 1f / settings.UvScale : 0f;

                for (var i = 0; i < count && !IsFull; i++)
                {
                    if (i > 0)
                    {
                        arcLength += Vector3.Distance(points[i - 1], points[i]);
                    }

                    positions.Add(points[i]);
                    radii.Add(float.Lerp(startRadius, endRadius, i / (float)(count - 1)));
                    textureCoordinates.Add((arcLength * oneOverUvScale) + settings.UvOffset);
                    segmentIds.Add(segmentId);
                }

                // Branches follow the whole parent run so every run stays contiguous
                foreach (var branch in branches)
                {
                    var radius = float.Lerp(startRadius, endRadius, branch.Fraction);
                    AddBolt(branch.Start, branch.End, branch.Depth, branch.Offset, radius, endRadius, branch.FirstLevel, branchLevel + 1);
                }
            }

            /// <summary>A branch decided while subdividing, built once its parent run is written.</summary>
            private readonly record struct Branch(Vector3 Start, Vector3 End, int Depth, float Offset, float Fraction, int FirstLevel);

            /// <summary>
            /// Fills the interior points by halving every span in turn, pushing each new midpoint off
            /// the line by a random amount up to the offset, which decays once per level. Each new
            /// midpoint rolls for a branch, collected into <paramref name="branches"/>.
            /// </summary>
            private void Displace(Vector3[] points, Vector3 end, Vector3 axis, float offset, int firstLevel, int branchLevel, List<Branch> branches)
            {
                var displacement = offset;
                var canBranch = branchLevel < MaxBranchLevel && settings.SplitRate > 0f;
                var level = firstLevel;

                for (var step = points.Length - 1; step > 1; step /= 2, level++)
                {
                    var half = step / 2;
                    var splitChance = settings.SplitRate * MathF.Pow(settings.RecursionSplitScale, level);

                    for (var i = half; i < points.Length - 1; i += step)
                    {
                        var before = points[i - half];
                        var after = points[i + half];
                        var direction = MathUtils.SafeNormalize(after - before, axis, ParticleMath.MinimumLengthSquared);
                        var push = ((random() * 2f) - 1f) * displacement;

                        points[i] = ((before + after) * 0.5f) + (RandomPerpendicular(direction) * push);

                        if (canBranch && random() < splitChance)
                        {
                            branches.Add(SplitBranch(points[i], after, end, direction, displacement * settings.OffsetDecay,
                                BitOperations.Log2((uint)half), i / (float)(points.Length - 1), level));
                        }
                    }

                    displacement *= settings.OffsetDecay;
                }
            }

            /// <summary>
            /// Shapes a branch splitting off at <paramref name="origin"/>, a new midpoint whose span
            /// runs on to <paramref name="spanEnd"/>. The branch reaches as far as that rest of the span,
            /// with the recursion levels and offset left below this one.
            /// </summary>
            private Branch SplitBranch(Vector3 origin, Vector3 spanEnd, Vector3 boltEnd, Vector3 spanDirection, float offset, int depth, float fraction, int level)
            {
                var heading = settings.BranchBehavior == ParticleLightningBranchBehavior.PARTICLE_LIGHTNING_BRANCH_ENDPOINT_DIR
                    ? MathUtils.SafeNormalize(boltEnd - origin, spanDirection, ParticleMath.MinimumLengthSquared)
                    : spanDirection;

                // Twist tilts the branch off its heading by a random sideways vector
                var offsetScale = MathF.Pow(settings.BranchOffsetScale, level + 1);
                var twist = settings.BranchTwist * offsetScale;
                heading = MathUtils.SafeNormalize(heading + (RandomPerpendicular(heading) * twist), heading, ParticleMath.MinimumLengthSquared);

                var reach = Vector3.Distance(origin, spanEnd) * MathF.Pow(settings.BranchDistanceScale, level + 1);

                return new Branch(origin, origin + (heading * reach), Math.Max(1, depth), offset * offsetScale, fraction, level + 1);
            }

            private Vector3 RandomPerpendicular(Vector3 direction)
            {
                var reference = MathF.Abs(direction.Z) < 0.99f ? Vector3.UnitZ : Vector3.UnitX;
                var u = Vector3.Normalize(Vector3.Cross(direction, reference));
                var v = Vector3.Cross(direction, u);
                var (sin, cos) = MathF.SinCos(random() * MathF.Tau);

                return (u * cos) + (v * sin);
            }

            public ParticleSnapshot ToSnapshot()
            {
                var columns = new Dictionary<(string Name, string Type), IEnumerable>
                {
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Position)!, "float3")] = positions.ToArray(),
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.Radius)!, "float")] = radii.ToArray(),
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.ScratchFloat)!, "float")] = textureCoordinates.ToArray(),
                    [(ParticleSnapshot.GetSnapshotAttributeName(ParticleField.RopeSegmentId)!, "int")] = segmentIds.ToArray(),
                };

                return ParticleSnapshot.Create((uint)positions.Count, columns);
            }
        }
    }
}

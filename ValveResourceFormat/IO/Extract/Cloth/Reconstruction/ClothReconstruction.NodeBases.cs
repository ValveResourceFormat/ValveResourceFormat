using System.Linq;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using static ValveResourceFormat.IO.FeModelIndex;

namespace ValveResourceFormat.IO
{
    internal sealed partial class ClothReconstruction
    {
        /// <summary>The margin a node-base scan decision has to clear to count as settled.</summary>
        private const float NodeBaseTieMargin = 1e-4f;

        /// <summary>The ring rolls in degrees tried to settle a node-base tie, widest first.</summary>
        private static readonly float[] NodeBaseNudgeLadder =
        [
            0.016f, -0.016f, 0.012f, -0.012f, 0.008f, -0.008f, 0.004f, -0.004f,
            0.002f, -0.002f, 0.001f, -0.001f, 0.0005f, -0.0005f,
        ];

        /// <summary>The furthest a roll may move a node or change a rod's rest length.</summary>
        private const float NodeBaseCostBudget = 5e-4f;

        private const float NodeBaseDegenerateAxis = 0.05f;
        private const float NodeBaseFoldResidual = 1e-4f;

        private Dictionary<int, List<int>>? nodeNeighbours;

        /// <summary>
        /// A joint's compiled node base with the candidate list scanned for it and the two joints the list is drawn from.
        /// </summary>
        private readonly record struct NodeBaseTarget(int Node, List<int> Candidates, NodeBasis Want,
            BoneChainJoint First, BoneChainJoint Second);

        /// <summary>
        /// Rolls the extruded ring of a chain joint whose <c>m_NodeBases</c> axis scan is a numerical tie onto the axis
        /// pair the compiled model kept, recording the roll in <see cref="BoneChainJoint.ExtrudeTwistTieNudge"/>.
        /// </summary>
        private void SteerNodeBaseTies(BoneChain chain)
        {
            if (Index.NodeBases.Count == 0)
            {
                return;
            }

            var targets = new List<NodeBaseTarget>();
            for (var i = 0; i < chain.Joints.Count; i++)
            {
                var joint = chain.Joints[i];
                if (!Index.NodeBases.TryGetValue(joint.Node, out var want))
                {
                    continue;
                }

                var child = i + 1 < chain.Joints.Count && chain.Joints[i + 1].ParentNode == joint.Node
                    ? chain.Joints[i + 1]
                    : null;
                var first = child is not null ? joint : chain.Joints.Find(other => other.Node == joint.ParentNode);
                if (first is null)
                {
                    continue;
                }

                var second = child ?? joint;

                var neighbours = NodeNeighbours(joint.Node);
                var candidates = neighbours.Count >= 3 && NodeBaseContains(neighbours, want)
                    ? neighbours
                    : NodeBaseCandidates(first, second);

                if (candidates is null || !NodeBaseContains(candidates, want))
                {
                    var parent = chain.Joints.Find(other => other.Node == first.ParentNode);
                    candidates = parent is not null ? NodeBaseCandidates(parent, first, second) : null;
                    if (candidates is null || !NodeBaseContains(candidates, want))
                    {
                        continue;
                    }
                }

                targets.Add(new NodeBaseTarget(joint.Node, candidates, want, first, second));
            }

            var moved = new Dictionary<int, Vector3>();
            foreach (var target in targets)
            {
                var scan = PredictNodeBase(target.Candidates, target.Node, moved, target.Want);

                if ((scan.Basis == target.Want && scan.Decided == NodeBaseScan.Decisions) || scan.DecidedAgainst
                    || target.Want.NodeX1 > target.Want.NodeX0)
                {
                    continue;
                }

                if (!TryNudgeNodeBase(target.Second, target, targets, moved, scan))
                {
                    TryNudgeNodeBase(target.First, target, targets, moved, scan);
                }
            }
        }

        private bool TryNudgeNodeBase(BoneChainJoint joint, NodeBaseTarget target, List<NodeBaseTarget> targets,
            Dictionary<int, Vector3> moved, NodeBaseScan before)
        {
            var ring = ProxyRingOf(joint.Node);
            if (ring.Count == 0 || IsHingedJoint(joint.Node) || joint.Node >= Index.InitPoseRotations.Length
                || ring.Exists(node => node >= Index.InitPosePositions.Length)
                || NodeBaseRingIsReadElsewhere(ring, targets))
            {
                return false;
            }

            var axis = Vector3.Transform(Vector3.UnitX,
                Index.InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis));
            if (axis.LengthSquared() <= 0f)
            {
                return false;
            }

            axis = Vector3.Normalize(axis);
            var pivot = Index.InitPosePositions[joint.Node];

            foreach (var nudge in NodeBaseNudgeLadder)
            {
                var probe = new Dictionary<int, Vector3>(moved);
                var rotation = Quaternion.CreateFromAxisAngle(axis,
                    float.DegreesToRadians(joint.ExtrudeTwistTieNudge + nudge));
                foreach (var node in ring)
                {
                    probe[node] = pivot + Vector3.Transform(Index.InitPosePositions[node] - pivot, rotation);
                }

                var after = PredictNodeBase(target.Candidates, target.Node, probe, target.Want);
                if (after.Basis != target.Want || !after.NoWorseThan(before) || after.Decided <= before.Decided
                    || !NodeBaseRollAffordable(probe) || NodeBaseRollRegresses(targets, moved, probe))
                {
                    continue;
                }

                joint.ExtrudeTwistTieNudge += nudge;
                foreach (var (node, position) in probe)
                {
                    moved[node] = position;
                }

                return true;
            }

            return false;
        }

        private bool NodeBaseRollAffordable(Dictionary<int, Vector3> probe)
        {
            foreach (var (node, position) in probe)
            {
                if (Vector3.Distance(Index.InitPosePositions[node], position) > NodeBaseCostBudget)
                {
                    return false;
                }
            }

            foreach (var rod in Index.Rods)
            {
                if ((!probe.ContainsKey(rod.NodeA) && !probe.ContainsKey(rod.NodeB))
                    || rod.NodeA >= Index.InitPosePositions.Length || rod.NodeB >= Index.InitPosePositions.Length)
                {
                    continue;
                }

                var rest = Vector3.Distance(Index.InitPosePositions[rod.NodeA], Index.InitPosePositions[rod.NodeB]);
                var rolled = Vector3.Distance(RestPosition(rod.NodeA, probe), RestPosition(rod.NodeB, probe));
                if (MathF.Abs(rolled - rest) > NodeBaseCostBudget * MathF.Max(1f, rest))
                {
                    return false;
                }
            }

            return true;
        }

        private bool NodeBaseRingIsReadElsewhere(List<int> ring, List<NodeBaseTarget> targets)
        {
            foreach (var (node, basis) in Index.NodeBases)
            {
                if (targets.Exists(target => target.Node == node))
                {
                    continue;
                }

                if (ring.Contains(basis.NodeX0) || ring.Contains(basis.NodeX1)
                    || ring.Contains(basis.NodeY0) || ring.Contains(basis.NodeY1))
                {
                    return true;
                }
            }

            return false;
        }

        private bool NodeBaseRollRegresses(List<NodeBaseTarget> targets,
            Dictionary<int, Vector3> moved, Dictionary<int, Vector3> probe)
        {
            foreach (var target in targets)
            {
                var before = PredictNodeBase(target.Candidates, target.Node, moved, target.Want);
                var after = PredictNodeBase(target.Candidates, target.Node, probe, target.Want);
                if (!after.NoWorseThan(before)
                    || (before.Basis == target.Want && after.Basis != target.Want))
                {
                    return true;
                }
            }

            return false;
        }

        private static bool NodeBaseContains(List<int> candidates, NodeBasis want)
            => candidates.Contains(want.NodeX0) && candidates.Contains(want.NodeX1)
            && candidates.Contains(want.NodeY0) && candidates.Contains(want.NodeY1);

        /// <summary>
        /// Gets the node vector the extrusion pushes for a joint: its ring when two or more wide, else its node and ring.
        /// </summary>
        private static List<int>? ExtrusionVector(BoneChainJoint joint, List<int> ring)
        {
            if (joint.ExtrudeSides >= 2)
            {
                return ring.Count >= joint.ExtrudeSides ? ring.GetRange(0, joint.ExtrudeSides) : null;
            }

            List<int> vector = [joint.Node];
            vector.AddRange(ring.Take(joint.ExtrudeSides));
            return vector;
        }

        /// <summary>
        /// Gets the sorted neighbour set a node's basis is graded against: the node and every corner of each source
        /// element it belongs to.
        /// </summary>
        private List<int> NodeNeighbours(int node)
        {
            nodeNeighbours ??= BuildNodeNeighbours();
            return nodeNeighbours.TryGetValue(node, out var neighbours) ? neighbours : [];
        }

        private Dictionary<int, List<int>> BuildNodeNeighbours()
        {
            var sets = new Dictionary<int, SortedSet<int>>();

            void Join(IReadOnlyList<int> corners)
            {
                foreach (var a in corners)
                {
                    if (a < 0 || a >= Index.InitPosePositions.Length)
                    {
                        continue;
                    }

                    if (!sets.TryGetValue(a, out var set))
                    {
                        sets[a] = set = [a];
                    }

                    foreach (var b in corners)
                    {
                        if (b >= 0 && b < Index.InitPosePositions.Length)
                        {
                            set.Add(b);
                        }
                    }
                }
            }

            foreach (var face in Index.SourceFaces)
            {
                Join(face);
            }

            foreach (var (a, b) in Index.SourceSprings)
            {
                Join([a, b]);
            }

            var built = new Dictionary<int, List<int>>(sets.Count);
            foreach (var (node, set) in sets)
            {
                built[node] = [.. set];
            }

            return built;
        }

        /// <summary>
        /// The node list scanned for a joint's graded or fit-arm basis. It is sorted ascending, which puts the higher node
        /// of the winning pair in X0 and decides which pair an exact tie keeps.
        /// </summary>
        private List<int>? NodeBaseCandidates(params BoneChainJoint[] joints)
        {
            var candidates = new List<int>();
            foreach (var joint in joints)
            {
                if (ExtrusionVector(joint, ProxyRingOf(joint.Node)) is not { } vector)
                {
                    return null;
                }

                candidates.AddRange(vector);
            }

            if (candidates.Count < 3)
            {
                return null;
            }

            candidates.Sort();
            return candidates.TrueForAll(node => node < Index.InitPosePositions.Length) ? candidates : null;
        }

        /// <summary>
        /// A joint's ring by its skeleton parents, or, where none of the joint's <c>$cc</c> nodes is parented to it,
        /// the ring assigned to the joint's declaration.
        /// </summary>
        private List<int> ChainJointRing(BoneChainJoint joint)
            => ProxyRingOf(joint.Node) is { Count: > 0 } ring ? ring : [.. joint.RingNodes];

        /// <summary>
        /// The chain preset's scan list for <paramref name="joint"/> and its child: the joint's extrusion vector, then the
        /// child's, unsorted. Where two pairs tie, the scan keeps the later one and writes its later node as X0.
        /// </summary>
        private List<int>? ChainNodeBaseCandidates(BoneChainJoint joint, BoneChainJoint child)
        {
            var candidates = new List<int>();
            foreach (var member in (ReadOnlySpan<BoneChainJoint>)[joint, child])
            {
                if (ExtrusionVector(member, ChainJointRing(member)) is not { } vector)
                {
                    return null;
                }

                candidates.AddRange(vector);
            }

            return candidates.Count >= 3 && candidates.TrueForAll(node => node < Index.InitPosePositions.Length) ? candidates : null;
        }

        private Vector3 RestPosition(int node, Dictionary<int, Vector3> moved)
            => moved.TryGetValue(node, out var position) ? position : Index.InitPosePositions[node];

        /// <summary>
        /// The basis the compiler's scans write for one joint, with the signed margin of each of the four decisions behind
        /// it (X pair, Y pair, handedness, fold), positive towards the compiled basis.
        /// </summary>
        private readonly record struct NodeBaseScan(NodeBasis Basis, float XMargin, float YMargin, float Handedness, float Fold)
        {
            public const int Decisions = 4;

            public int Decided => State(XMargin) + State(YMargin) + State(Handedness) + State(Fold);

            public bool DecidedAgainst
                => State(XMargin) < 0 || State(YMargin) < 0 || State(Handedness) < 0 || State(Fold) < 0;

            public bool NoWorseThan(NodeBaseScan other)
                => State(XMargin) >= State(other.XMargin) && State(YMargin) >= State(other.YMargin)
                && State(Handedness) >= State(other.Handedness) && State(Fold) >= State(other.Fold);

            private static int State(float margin) => margin >= NodeBaseTieMargin ? 1 : margin <= -NodeBaseTieMargin ? -1 : 0;
        }

        /// <summary>
        /// Replays the compiler's two axis scans over <paramref name="candidates"/>, the handedness flip after them and the
        /// half-turn folds, scoring each decision against the compiled basis <paramref name="want"/> of <paramref name="node"/>.
        /// </summary>
        private NodeBaseScan PredictNodeBase(List<int> candidates, int node, Dictionary<int, Vector3> moved, NodeBasis want)
        {
            var (xOuter, xInner, xMargin) = ScanNodeBasePair(candidates, moved, NodeBaseSpan, want.NodeX1, want.NodeX0);
            var xAxis = RestPosition(xOuter, moved) - RestPosition(xInner, moved);

            var (yOuter, yInner, yMargin) = ScanNodeBasePair(candidates, moved,
                (a, b) => NodeBasePerpendicular(xAxis, a - b), want.NodeY1, want.NodeY0);
            var yAxis = RestPosition(yOuter, moved) - RestPosition(yInner, moved);

            var scalar = NodeBaseHandedness(xAxis, yAxis, node);
            var basis = scalar < 0f
                ? new NodeBasis(xInner, xOuter, yOuter, yInner)
                : new NodeBasis(xInner, xOuter, yInner, yOuter);

            var (folded, residual) = FoldNodeBase(basis, xAxis, yAxis, scalar < 0f, node);
            var handedness = MathF.Abs(scalar) / MathF.Max(xAxis.Length(), 1e-12f);
            var fold = NodeBaseFoldReaches(basis, want)
                ? (folded == want ? 1f : -1f) * MathF.Abs(residual - NodeBaseFoldResidual)
                : 0f;
            return new NodeBaseScan(folded, xMargin, yMargin, handedness, fold);
        }

        /// <summary>
        /// Applies Gram-Schmidt and the half-turn folds to a scanned basis. Returns the basis written and the smallest
        /// residual tested.
        /// </summary>
        private (NodeBasis Basis, float Residual) FoldNodeBase(NodeBasis basis, Vector3 xAxis, Vector3 yAxis,
            bool swapped, int node)
        {
            var up = NodeUpAxis(node);
            var y = MathUtils.SafeNormalize(yAxis, -Vector3.UnitZ);
            if (swapped)
            {
                y = -y;
            }

            var x = MathUtils.ProjectOntoPlane(xAxis, y);
            if (x.Length() <= NodeBaseDegenerateAxis)
            {
                var alt = Vector3.Cross(up, y);
                x = alt.Length() < 1e-3f
                    ? Vector3.Cross(y, MathF.Abs(y.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY)
                    : alt;
            }

            if (x.LengthSquared() <= 0f)
            {
                return (basis, float.PositiveInfinity);
            }

            x = Vector3.Normalize(x);
            var z = Vector3.Cross(x, y);
            var predicted = Quaternion.CreateFromRotationMatrix(new Matrix4x4(
                x.X, x.Y, x.Z, 0f, y.X, y.Y, y.Z, 0f, z.X, z.Y, z.Z, 0f, 0f, 0f, 0f, 1f));
            var orientation = node < Index.InitPoseRotations.Length ? Index.InitPoseRotations[node] : Quaternion.Identity;
            var adjust = Quaternion.Normalize(Quaternion.Conjugate(predicted) * orientation);

            var straight = NodeBaseResidual(adjust);
            var both = NodeBaseResidual(adjust * new Quaternion(0f, 0f, 1f, 0f));
            var acrossX = NodeBaseResidual(adjust * new Quaternion(0f, 1f, 0f, 0f));
            var acrossY = NodeBaseResidual(adjust * new Quaternion(1f, 0f, 0f, 0f));
            var residual = MathF.Min(MathF.Min(straight, both), MathF.Min(acrossX, acrossY));

            if (straight < NodeBaseFoldResidual)
            {
                return (basis, residual);
            }

            if (both < NodeBaseFoldResidual)
            {
                return (FoldBoth(basis), residual);
            }

            if (acrossX < NodeBaseFoldResidual)
            {
                return (FoldAcrossX(basis), residual);
            }

            if (acrossY < NodeBaseFoldResidual)
            {
                return (FoldAcrossY(basis), residual);
            }

            return (basis, residual);
        }

        private static NodeBasis FoldBoth(NodeBasis basis) => new(basis.NodeX1, basis.NodeX0, basis.NodeY1, basis.NodeY0);

        private static NodeBasis FoldAcrossX(NodeBasis basis) => new(basis.NodeX1, basis.NodeX0, basis.NodeY0, basis.NodeY1);

        private static NodeBasis FoldAcrossY(NodeBasis basis) => new(basis.NodeX0, basis.NodeX1, basis.NodeY1, basis.NodeY0);

        private Vector3 NodeUpAxis(int node)
            => node < Index.InitPoseRotations.Length ? Vector3.Transform(Vector3.UnitZ, Index.InitPoseRotations[node]) : Vector3.UnitZ;

        private static float NodeBaseResidual(Quaternion q)
            => MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));

        private static bool NodeBaseFoldReaches(NodeBasis basis, NodeBasis want)
            => want == basis || want == FoldBoth(basis) || want == FoldAcrossX(basis) || want == FoldAcrossY(basis);

        /// <summary>
        /// Scans every pair in list order keeping the last maximum, and returns it with the margin by which the wanted pair
        /// beats the pairs that could take the scan from it.
        /// </summary>
        private (int Outer, int Inner, float Margin) ScanNodeBasePair(List<int> candidates, Dictionary<int, Vector3> moved,
            Func<Vector3, Vector3, float> score, int wantOuter, int wantInner)
        {
            var lowWanted = Math.Min(wantOuter, wantInner);
            var highWanted = Math.Max(wantOuter, wantInner);
            var wanted = candidates.Contains(lowWanted) && candidates.Contains(highWanted)
                ? score(RestPosition(lowWanted, moved), RestPosition(highWanted, moved))
                : float.NegativeInfinity;

            var best = float.NegativeInfinity;
            var other = float.NegativeInfinity;
            var passedWanted = false;
            int outer = candidates[0], inner = candidates[0];
            for (var i = 0; i < candidates.Count; i++)
            {
                var a = RestPosition(candidates[i], moved);
                for (var j = i + 1; j < candidates.Count; j++)
                {
                    var value = score(a, RestPosition(candidates[j], moved));
                    if (value >= best)
                    {
                        best = value;
                        outer = candidates[i];
                        inner = candidates[j];
                    }

                    if ((candidates[i] == lowWanted && candidates[j] == highWanted)
                        || (candidates[i] == highWanted && candidates[j] == lowWanted))
                    {
                        passedWanted = true;
                    }
                    else if (passedWanted || value != wanted)
                    {
                        other = MathF.Max(other, value);
                    }
                }
            }

            return (outer, inner, wanted - other);
        }

        /// <summary>Gets the distance between two nodes, summed in the compiler's term order.</summary>
        private static float NodeBaseSpan(Vector3 a, Vector3 b)
        {
            var d = a - b;
            return MathF.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
        }

        /// <summary>Gets the length of <c>axis x d</c>, summed in the compiler's term order.</summary>
        private static float NodeBasePerpendicular(Vector3 axis, Vector3 d)
        {
            var y = (axis.X * d.Z) - (axis.Z * d.X);
            var z = (axis.Y * d.X) - (axis.X * d.Y);
            var x = (axis.Z * d.Y) - (axis.Y * d.Z);
            return MathF.Sqrt((y * y) + (z * z) + (x * x));
        }

        /// <summary>Gets the scalar triple product of X, unit Y and the node's up axis, summed in the compiler's term order.</summary>
        private float NodeBaseHandedness(Vector3 xAxis, Vector3 yAxis, int node)
        {
            var length = MathF.Sqrt((yAxis.Y * yAxis.Y) + (yAxis.Z * yAxis.Z) + (yAxis.X * yAxis.X));
            var y = length > 0f ? yAxis * (1f / length) : new Vector3(0f, 0f, -1f);
            var z = NodeUpAxis(node);

            return (((y.X * xAxis.Z) - (xAxis.X * y.Z)) * z.Y)
                + (((xAxis.X * y.Y) - (y.X * xAxis.Y)) * z.Z)
                + (((y.Z * xAxis.Y) - (xAxis.Z * y.Y)) * z.X);
        }
    }
}

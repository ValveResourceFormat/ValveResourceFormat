using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        // The gap two candidate pairs must score apart before the round trip's own rest-position drift can
        // still swap which one the scan keeps, and the roll ladder that opens such a gap.
        const float NodeBaseTieMargin = 1e-4f;
        static readonly float[] NodeBaseNudgeLadder =
        [
            0.0005f, -0.0005f, 0.001f, -0.001f, 0.002f, -0.002f,
            0.004f, -0.004f, 0.008f, -0.008f, 0.012f, -0.012f, 0.016f, -0.016f,
        ];

        // Half the 1e-3 absolute floor the comparator holds two floats equal within, so a roll can never
        // push a component the round trip already carries drift on across it.
        const float NodeBaseCostBudget = 5e-4f;

        // One joint's shipped node base together with the candidate list the compiler scanned for it and
        // the two joints whose scan vectors that list is drawn from.
        readonly record struct NodeBaseTarget(int Node, List<int> Candidates, NodeBasis Want,
            BoneChainJoint First, BoneChainJoint Second);

        /// <summary>
        /// Rolls the extruded ring of a chain joint whose <c>m_NodeBases</c> axis scan is a numerical tie
        /// onto the axis pair the original kept, recording the roll in
        /// <see cref="BoneChainJoint.ExtrudeTwistTieNudge"/>.
        /// </summary>
        /// <remarks>
        /// The compiler picks a joint's two axes by scanning the joint's own extrude ring together with the
        /// next joint's, first for the longest pair and then for the pair most perpendicular to it, and
        /// flips the second onto the handedness of the joint's own bind Z. Both diagonals of a ring two
        /// nodes wide are the same length to within a float, so on those joints the winner is settled in
        /// the last bits of a float32 - below the precision the rest positions round-trip with - and
        /// rebuilding the same ring lands on the other diagonal about half the time. A roll of a few
        /// thousandths of a degree separates the two candidates by more than that drift. Only a joint whose
        /// scan <see cref="PredictNodeBase"/> resolves by less than <see cref="NodeBaseTieMargin"/> is
        /// rolled - which side that scan currently lands on carries no information there, since the drift
        /// alone re-decides it - and then only to the smallest roll that both decides the scan the
        /// original's way and leaves every geometry-derived array inside
        /// <see cref="NodeBaseCostBudget"/>.
        /// </remarks>
        void SteerNodeBaseTies(BoneChain chain)
        {
            if (NodeBases.Count == 0)
            {
                return;
            }

            var targets = new List<NodeBaseTarget>();
            for (var i = 0; i < chain.Joints.Count; i++)
            {
                var joint = chain.Joints[i];
                if (!NodeBases.TryGetValue(joint.Node, out var want))
                {
                    continue;
                }

                // A joint's scan vectors are its own ring and its first child's; a joint with no child of
                // its own is scanned over the last link it belongs to, which is its parent's.
                var child = i + 1 < chain.Joints.Count && chain.Joints[i + 1].ParentNode == joint.Node
                    ? chain.Joints[i + 1]
                    : null;
                var first = child is not null ? joint : chain.Joints.Find(other => other.Node == joint.ParentNode);
                if (first is null || NodeBaseCandidates(first, child ?? joint) is not { } candidates
                    || !NodeBaseContains(candidates, want))
                {
                    continue;
                }

                targets.Add(new NodeBaseTarget(joint.Node, candidates, want, first, child ?? joint));
            }

            var moved = new Dictionary<int, Vector3>();
            foreach (var target in targets)
            {
                var scan = PredictNodeBase(target.Candidates, target.Node, moved, target.Want);

                // A decision already standing against the original is not a tie the drift could have
                // turned over, and the X ordering comes from the candidates' list positions rather than
                // from the geometry, so no roll reaches either.
                if ((scan.Basis == target.Want && scan.Decided == 3) || scan.DecidedAgainst
                    || target.Candidates.IndexOf(target.Want.NodeX1) > target.Candidates.IndexOf(target.Want.NodeX0))
                {
                    continue;
                }

                if (!TryNudgeNodeBase(target.Second, target, targets, moved, scan))
                {
                    TryNudgeNodeBase(target.First, target, targets, moved, scan);
                }
            }
        }

        bool TryNudgeNodeBase(BoneChainJoint joint, NodeBaseTarget target, List<NodeBaseTarget> targets,
            Dictionary<int, Vector3> moved, NodeBaseScan before)
        {
            // A hinged joint's own ring is laid along its hinge vector rather than by the authored roll, so
            // the roll no longer moves the ring the scan reads.
            var ring = ProxyRingOf(joint.Node);
            if (ring.Count == 0 || IsHingedJoint(joint.Node) || joint.Node >= InitPoseRotations.Length
                || ring.Exists(node => node >= InitPosePositions.Length)
                || NodeBaseRingIsReadElsewhere(ring, targets))
            {
                return false;
            }

            var axis = Vector3.Transform(Vector3.UnitX,
                InitPoseRotations[joint.Node] * ExtrudeAxisSelectQuaternion(joint.ForwardAxis));
            if (axis.LengthSquared() <= 0f)
            {
                return false;
            }

            axis = Vector3.Normalize(axis);
            var pivot = InitPosePositions[joint.Node];

            foreach (var nudge in NodeBaseNudgeLadder)
            {
                var probe = new Dictionary<int, Vector3>(moved);
                var rotation = Quaternion.CreateFromAxisAngle(axis,
                    float.DegreesToRadians(joint.ExtrudeTwistTieNudge + nudge));
                foreach (var node in ring)
                {
                    probe[node] = pivot + Vector3.Transform(InitPosePositions[node] - pivot, rotation);
                }

                // The roll has to settle a decision that was open and may not unsettle one that was not.
                // A scan a roll cannot move at all - a ring square to its own segment leaves both diagonals
                // exactly equal whatever the roll - stays open either way, and holding the other decisions
                // hostage to it only gives up their steer as well.
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

        // Every array the rolled nodes feed, held to half the tolerance it is compared at: a displacement
        // under the absolute floor covers each node's own m_InitPose position and its m_CtrlOffsets /
        // m_CtrlSoftOffsets vOffset in any parent's frame at once, since no component of a vector can
        // exceed its length; the rods spanning them are measured directly.
        bool NodeBaseRollAffordable(Dictionary<int, Vector3> probe)
        {
            foreach (var (node, position) in probe)
            {
                if (Vector3.Distance(InitPosePositions[node], position) > NodeBaseCostBudget)
                {
                    return false;
                }
            }

            foreach (var rod in Rods)
            {
                if (!probe.ContainsKey(rod.NodeA) && !probe.ContainsKey(rod.NodeB))
                {
                    continue;
                }

                if (rod.NodeA >= InitPosePositions.Length || rod.NodeB >= InitPosePositions.Length)
                {
                    continue;
                }

                var rest = Vector3.Distance(InitPosePositions[rod.NodeA], InitPosePositions[rod.NodeB]);
                var rolled = Vector3.Distance(RestPosition(rod.NodeA, probe), RestPosition(rod.NodeB, probe));
                if (MathF.Abs(rolled - rest) > NodeBaseCostBudget * MathF.Max(1f, rest))
                {
                    return false;
                }
            }

            return true;
        }

        // Whether the original scanned this ring for a node base this steer does not model - a 1-wide
        // stretch of chain reaches further along itself than the two vectors modelled here, and a ring an
        // unmodelled entry reads is one whose roll cannot be checked for a regression, so it is left alone.
        bool NodeBaseRingIsReadElsewhere(List<int> ring, List<NodeBaseTarget> targets)
        {
            foreach (var (node, basis) in NodeBases)
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

        // A roll moves the rings two of the chain's node bases are scanned over, so it is only taken when
        // every OTHER entry keeps both the answer it lands on and the firmness it lands on it with.
        bool NodeBaseRollRegresses(List<NodeBaseTarget> targets,
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

        static bool NodeBaseContains(List<int> candidates, NodeBasis want)
            => candidates.Contains(want.NodeX0) && candidates.Contains(want.NodeX1)
            && candidates.Contains(want.NodeY0) && candidates.Contains(want.NodeY1);

        // The two node vectors the extrusion fills for a joint, in the order it pushes them: a joint two or
        // more nodes wide contributes its ring alone, a narrower one contributes its own control node.
        List<int>? NodeBaseVector(BoneChainJoint joint)
        {
            var ring = ProxyRingOf(joint.Node);
            if (joint.ExtrudeSides >= 2)
            {
                return ring.Count >= joint.ExtrudeSides ? ring.GetRange(0, joint.ExtrudeSides) : null;
            }

            List<int> vector = [joint.Node];
            vector.AddRange(ring.Take(joint.ExtrudeSides));
            return vector;
        }

        List<int>? NodeBaseCandidates(BoneChainJoint first, BoneChainJoint second)
        {
            if (NodeBaseVector(first) is not { } a || NodeBaseVector(second) is not { } b || a.Count + b.Count < 3)
            {
                return null;
            }

            var candidates = new List<int>(a.Count + b.Count);
            candidates.AddRange(a);
            candidates.AddRange(b);
            return candidates.TrueForAll(node => node < InitPosePositions.Length) ? candidates : null;
        }

        Vector3 RestPosition(int node, Dictionary<int, Vector3> moved)
            => moved.TryGetValue(node, out var position) ? position : InitPosePositions[node];

        /// <summary>
        /// The basis the compiler's scans would write for one joint, and how firmly each of the three
        /// decisions behind it stands. Every margin is signed towards the original's own answer: the two
        /// scan margins are how far the original's pair beats the best other pair, the handedness how far
        /// the term ordering Y sits on the side that produces the original's ordering.
        /// </summary>
        readonly record struct NodeBaseScan(NodeBasis Basis, float XMargin, float YMargin, float Handedness)
        {
            public int Decided => State(XMargin) + State(YMargin) + State(Handedness);
            public bool NoWorseThan(NodeBaseScan other)
                => State(XMargin) >= State(other.XMargin) && State(YMargin) >= State(other.YMargin)
                && State(Handedness) >= State(other.Handedness);
            public bool DecidedAgainst
                => State(XMargin) < 0 || State(YMargin) < 0 || State(Handedness) < 0;
            static int State(float margin) => margin >= NodeBaseTieMargin ? 1 : margin <= -NodeBaseTieMargin ? -1 : 0;
        }

        /// <summary>
        /// Runs the compiler's own two axis scans and the handedness flip that follows them over
        /// <paramref name="candidates"/>, scoring the result against the basis <paramref name="want"/> the
        /// original wrote for <paramref name="node"/>.
        /// </summary>
        NodeBaseScan PredictNodeBase(List<int> candidates, int node, Dictionary<int, Vector3> moved, NodeBasis want)
        {
            var (xOuter, xInner, xMargin) = ScanNodeBasePair(candidates, moved,
                static (a, b) => NodeBaseSpan(a, b), want.NodeX1, want.NodeX0);
            var xAxis = RestPosition(xOuter, moved) - RestPosition(xInner, moved);

            var (yOuter, yInner, yMargin) = ScanNodeBasePair(candidates, moved,
                (a, b) => NodeBasePerpendicular(xAxis, a - b), want.NodeY1, want.NodeY0);
            var yAxis = RestPosition(yOuter, moved) - RestPosition(yInner, moved);

            var scalar = NodeBaseHandedness(xAxis, yAxis, node);
            var basis = scalar < 0f
                ? new NodeBasis(xInner, xOuter, yOuter, yInner)
                : new NodeBasis(xInner, xOuter, yInner, yOuter);
            var wantsPositive = candidates.IndexOf(want.NodeY1) < candidates.IndexOf(want.NodeY0);
            var handedness = scalar / MathF.Max(xAxis.Length(), 1e-12f);
            return new NodeBaseScan(basis, xMargin, yMargin, wantsPositive ? handedness : -handedness);
        }

        // The scan takes i outer and j = i+1 inner, both ascending, and keeps the LAST maximum, so a pair
        // that only ties the running best still replaces it.
        (int Outer, int Inner, float Margin) ScanNodeBasePair(List<int> candidates, Dictionary<int, Vector3> moved,
            Func<Vector3, Vector3, float> score, int wantOuter, int wantInner)
        {
            var best = float.NegativeInfinity;
            var wanted = float.NegativeInfinity;
            var other = float.NegativeInfinity;
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

                    if ((candidates[i] == wantOuter && candidates[j] == wantInner)
                        || (candidates[i] == wantInner && candidates[j] == wantOuter))
                    {
                        wanted = value;
                    }
                    else
                    {
                        other = MathF.Max(other, value);
                    }
                }
            }

            return (outer, inner, wanted - other);
        }

        // The three scores below are summed in the compiler's own order; regrouping them moves the result
        // by the last bit, which is the whole quantity these scans are decided on.
        static float NodeBaseSpan(Vector3 a, Vector3 b)
        {
            var d = a - b;
            return MathF.Sqrt((d.X * d.X) + (d.Y * d.Y) + (d.Z * d.Z));
        }

        static float NodeBasePerpendicular(Vector3 axis, Vector3 d)
        {
            var y = (axis.X * d.Z) - (axis.Z * d.X);
            var z = (axis.Y * d.X) - (axis.X * d.Y);
            var x = (axis.Z * d.Y) - (axis.Y * d.Z);
            return MathF.Sqrt((y * y) + (z * z) + (x * x));
        }

        float NodeBaseHandedness(Vector3 xAxis, Vector3 yAxis, int node)
        {
            var length = MathF.Sqrt((yAxis.Y * yAxis.Y) + (yAxis.Z * yAxis.Z) + (yAxis.X * yAxis.X));
            var y = length > 0f ? yAxis * (1f / length) : new Vector3(0f, 0f, -1f);
            var z = node < InitPoseRotations.Length
                ? Vector3.Transform(Vector3.UnitZ, InitPoseRotations[node])
                : Vector3.UnitZ;

            return (((y.X * xAxis.Z) - (xAxis.X * y.Z)) * z.Y)
                + (((xAxis.X * y.Y) - (y.X * xAxis.Y)) * z.Z)
                + (((y.Z * xAxis.Y) - (xAxis.Z * y.Y)) * z.X);
        }
    }
}

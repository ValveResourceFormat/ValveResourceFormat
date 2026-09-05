using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        // The gap two candidate pairs must score apart before the round trip's own rest-position drift can
        // still swap which one the scan keeps, and the roll ladder that opens such a gap. The ladder runs
        // widest first: how far a roll of a given size moves a recompiled ring is only approximated here,
        // so the roll that survives the round trip is the widest one the cost budget still allows, not the
        // narrowest one that clears the margin in this model of it.
        const float NodeBaseTieMargin = 1e-4f;
        static readonly float[] NodeBaseNudgeLadder =
        [
            0.016f, -0.016f, 0.012f, -0.012f, 0.008f, -0.008f, 0.004f, -0.004f,
            0.002f, -0.002f, 0.001f, -0.001f, 0.0005f, -0.0005f,
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
        /// alone re-decides it - and then to the widest roll that both decides the scan the original's way
        /// and leaves every geometry-derived array inside <see cref="NodeBaseCostBudget"/>.
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
                if (first is null)
                {
                    continue;
                }

                var second = child ?? joint;

                // The compiler grades over the node's own neighbour set wherever that set has three or
                // more members; the chain's extrusion vectors below are the fallback for a node the
                // source elements do not reach.
                var neighbours = NodeNeighbours(joint.Node);
                var candidates = neighbours.Count >= 3 && NodeBaseContains(neighbours, want)
                    ? neighbours
                    : NodeBaseCandidates(first, second);

                // A stretch of chain one node wide reaches one link further back: where the two vectors
                // above cannot even hold the references the original wrote, the parent's own vector is
                // taken in as well. Widening only where the narrow list is refuted by the original itself
                // keeps a joint whose narrow list does explain it on the narrow one.
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

                // A joint is left alone when its scan already lands on the original with every decision
                // firm, when a decision stands against the original by more than the drift could have
                // turned over, and when the original's X pair is stored in ascending node order.
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

        /// <summary>
        /// The neighbour set the compiler grades a node's basis against: one sorted vector per node,
        /// seeded with the node itself, into which every source element the node belongs to inserts all of
        /// its own corners. A two-corner element contributes a genuine neighbour pair, which is why an
        /// authored spring moves a basis and the surface clique alone does not describe the set.
        /// </summary>
        List<int> NodeNeighbours(int node)
        {
            nodeNeighbours ??= BuildNodeNeighbours();
            return nodeNeighbours.TryGetValue(node, out var neighbours) ? neighbours : [];
        }

        Dictionary<int, List<int>>? nodeNeighbours;

        Dictionary<int, List<int>> BuildNodeNeighbours()
        {
            var sets = new Dictionary<int, SortedSet<int>>();

            void Join(IReadOnlyList<int> corners)
            {
                foreach (var a in corners)
                {
                    if (a < 0 || a >= InitPosePositions.Length)
                    {
                        continue;
                    }

                    if (!sets.TryGetValue(a, out var set))
                    {
                        sets[a] = set = [a];
                    }

                    foreach (var b in corners)
                    {
                        if (b >= 0 && b < InitPosePositions.Length)
                        {
                            set.Add(b);
                        }
                    }
                }
            }

            foreach (var face in SourceFaces)
            {
                Join(face);
            }

            foreach (var (a, b) in SourceSprings)
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
        /// The node list <see cref="ScanNodeBasePair"/> scans for a joint, ascending by node index. The
        /// compiler scans a set sorted that way, which is what puts the higher node index of the winning
        /// pair in X0 and settles which pair an exact tie keeps.
        /// </summary>
        List<int>? NodeBaseCandidates(params BoneChainJoint[] joints)
        {
            var candidates = new List<int>();
            foreach (var joint in joints)
            {
                if (NodeBaseVector(joint) is not { } vector)
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
            return candidates.TrueForAll(node => node < InitPosePositions.Length) ? candidates : null;
        }

        Vector3 RestPosition(int node, Dictionary<int, Vector3> moved)
            => moved.TryGetValue(node, out var position) ? position : InitPosePositions[node];

        /// <summary>
        /// The basis the compiler's scans would write for one joint, and how firmly each of the four
        /// decisions behind it stands. The two scan margins are how far the original's own pair beats the
        /// best pair that would take the scan from it, signed towards the original. The handedness is the
        /// unsigned distance of the scalar triple product from zero. The fold is the distance of the
        /// residual rotation from the threshold below which the compiler records it as a pair swap, signed
        /// towards the original's ordering and zero where no fold could produce that ordering.
        /// </summary>
        readonly record struct NodeBaseScan(NodeBasis Basis, float XMargin, float YMargin, float Handedness, float Fold)
        {
            public const int Decisions = 4;
            public int Decided => State(XMargin) + State(YMargin) + State(Handedness) + State(Fold);
            public bool NoWorseThan(NodeBaseScan other)
                => State(XMargin) >= State(other.XMargin) && State(YMargin) >= State(other.YMargin)
                && State(Handedness) >= State(other.Handedness) && State(Fold) >= State(other.Fold);
            public bool DecidedAgainst
                => State(XMargin) < 0 || State(YMargin) < 0 || State(Handedness) < 0 || State(Fold) < 0;
            static int State(float margin) => margin >= NodeBaseTieMargin ? 1 : margin <= -NodeBaseTieMargin ? -1 : 0;
        }

        // Below this the compiler throws its Gram-Schmidt X axis away and rebuilds one from the node's own
        // up vector, and below this residual rotation it records a near-half-turn as a pair swap instead.
        const float NodeBaseDegenerateAxis = 0.05f;
        const float NodeBaseFoldResidual = 1e-4f;

        /// <summary>
        /// Runs the compiler's own two axis scans, the handedness flip that follows them and the pair swaps
        /// it folds a near-half-turn residual into over <paramref name="candidates"/>, scoring the result
        /// against the basis <paramref name="want"/> the original wrote for <paramref name="node"/>.
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

            // The handedness only says how far the flip sits from its own zero; whether the ordering it
            // produced is the original's is carried by the fold term, which is the one a roll can move -
            // but only where a fold could produce the original's ordering at all. Where the original names
            // different NODES the scans are what disagree, and the fold has nothing to say about it.
            var (folded, residual) = FoldNodeBase(basis, xAxis, yAxis, scalar < 0f, node);
            var handedness = MathF.Abs(scalar) / MathF.Max(xAxis.Length(), 1e-12f);
            var fold = NodeBaseFoldReaches(basis, want)
                ? (folded == want ? 1f : -1f) * MathF.Abs(residual - NodeBaseFoldResidual)
                : 0f;
            return new NodeBaseScan(folded, xMargin, yMargin, handedness, fold);
        }

        /// <summary>
        /// Applies the compiler's basis tail to a scanned pair ordering: Gram-Schmidt with the degenerate
        /// fallback, then the four half-turn folds that replace a residual rotation with a pair swap and an
        /// identity <c>qAdjust</c>. Returns the ordering it writes and the smallest residual it tested,
        /// which is what a roll has to push clear of <see cref="NodeBaseFoldResidual"/>.
        /// </summary>
        (NodeBasis Basis, float Residual) FoldNodeBase(NodeBasis basis, Vector3 xAxis, Vector3 yAxis,
            bool swapped, int node)
        {
            var up = node < InitPoseRotations.Length
                ? Vector3.Transform(Vector3.UnitZ, InitPoseRotations[node])
                : Vector3.UnitZ;
            var y = yAxis.Length() > 0f ? Vector3.Normalize(yAxis) : new Vector3(0f, 0f, -1f);
            if (swapped)
            {
                y = -y;
            }

            var x = xAxis - (y * Vector3.Dot(y, xAxis));
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
            var orientation = node < InitPoseRotations.Length ? InitPoseRotations[node] : Quaternion.Identity;
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
                return (new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY1, basis.NodeY0), residual);
            }

            if (acrossX < NodeBaseFoldResidual)
            {
                return (new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY0, basis.NodeY1), residual);
            }

            if (acrossY < NodeBaseFoldResidual)
            {
                return (new NodeBasis(basis.NodeX0, basis.NodeX1, basis.NodeY1, basis.NodeY0), residual);
            }

            return (basis, residual);
        }

        static float NodeBaseResidual(Quaternion q)
            => MathF.Sqrt((q.X * q.X) + (q.Y * q.Y) + (q.Z * q.Z));

        // The four orderings the folds can write from one scanned pair of pairs.
        static bool NodeBaseFoldReaches(NodeBasis basis, NodeBasis want)
            => want == basis
            || want == new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY1, basis.NodeY0)
            || want == new NodeBasis(basis.NodeX1, basis.NodeX0, basis.NodeY0, basis.NodeY1)
            || want == new NodeBasis(basis.NodeX0, basis.NodeX1, basis.NodeY1, basis.NodeY0);

        // The scan takes i outer and j = i+1 inner, both ascending, and keeps the LAST maximum, so a pair
        // that only ties the running best still replaces it. A pair scanned BEFORE the original's own pair
        // and scoring exactly equal to it therefore loses to it and is left out of the margin. On a chain
        // that case is the rule rather than the exception: two candidate pairs that differ only by which
        // end of one ring they use score the same to the last bit however the rings are rolled, so counting
        // them as competition pins the margin at zero and no roll can ever be seen to settle anything.
        (int Outer, int Inner, float Margin) ScanNodeBasePair(List<int> candidates, Dictionary<int, Vector3> moved,
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

                    if (candidates[i] == lowWanted && candidates[j] == highWanted)
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

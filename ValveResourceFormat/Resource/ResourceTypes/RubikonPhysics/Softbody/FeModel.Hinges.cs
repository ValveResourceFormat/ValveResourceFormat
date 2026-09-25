using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Gets whether <paramref name="bend"/> is a ring bend laid over a chain: its hub's joint lies between the joints
        /// owning its first and second ends.
        /// </summary>
        internal bool IsChainRingBend(KelagerBend bend)
        {
            var hub = BendOwner(bend.MidNode);
            var first = BendOwner(bend.End0);
            var second = BendOwner(bend.End1);
            return hub >= 0 && first >= 0 && second >= 0
                && hub < SkelParents.Length && SkelParents[hub] == first
                && second < SkelParents.Length && SkelParents[second] == hub;
        }

        /// <summary>
        /// Gets whether any <see cref="KelagerBends"/> record is a chain ring bend (<see cref="IsChainRingBend"/>), which only
        /// <c>rigid_edge_hinges</c> builds.
        /// </summary>
        internal bool HasChainRingBends => KelagerBends.Any(IsChainRingBend);

        private int BendOwner(int node)
            => node < 0 || node >= CtrlNames.Length ? -1
                : !IsProxyNodeName(CtrlNames[node]) ? node
                : node < SkelParents.Length ? SkelParents[node] : -1;

        /// <summary>
        /// Recovers the <c>stiff_hinge</c> stiffness, <c>stiff_hinge_angle</c> in degrees and <c>motion_bias</c> of the
        /// joint at <paramref name="jointNode"/> from the bend whose first end is the joint or one of its proxies, or null
        /// when it has none.
        /// </summary>
        /// <param name="jointNode">The node of the joint whose stiff hinge to recover.</param>
        /// <param name="rank">Which of the joint's bends to read, in declaration order.</param>
        internal (float Stiffness, float Angle, float MotionBias)? GetStiffHinge(int jointNode, int rank = 0)
        {
            var seen = 0;

            foreach (var bend in KelagerBends)
            {
                var owner = bend.End0 >= 0 && bend.End0 < CtrlNames.Length && IsProxyNodeName(CtrlNames[bend.End0])
                    && bend.End0 < SkelParents.Length
                        ? SkelParents[bend.End0]
                        : -1;
                if ((bend.End0 != jointNode && owner != jointNode) || IsChainRingBend(bend))
                {
                    continue;
                }

                var midMass = InverseMassOf(bend.MidNode);
                if ((4f * midMass) + InverseMassOf(bend.End0) + InverseMassOf(bend.End1) <= 0f)
                {
                    continue;
                }

                var stiffness = (bend.End0Weight + bend.End1Weight - (2f * bend.MidWeight)) / 3f;
                if (stiffness <= 0f)
                {
                    continue;
                }

                var fullBias = midMass > 0f && MathF.Abs(bend.MidWeight) < FullMotionBiasEpsilon
                    && MathF.Abs(bend.End0Weight) > FullMotionBiasEpsilon;

                if (seen++ < rank)
                {
                    continue;
                }

                return (Math.Clamp(stiffness, 0f, 1f), BendAngle(bend), fullBias ? 1f : 0f);
            }

            return null;
        }

        private const float FullMotionBiasEpsilon = 1e-6f;

        private const float KelagerHeightFloor = 0.001f;

        private float BendAngle(KelagerBend bend)
        {
            if (bend.MidNode >= InitPosePositions.Length || bend.End0 >= InitPosePositions.Length
                || bend.End1 >= InitPosePositions.Length || bend.MidNode < 0 || bend.End0 < 0 || bend.End1 < 0)
            {
                return 0f;
            }

            var toEnd0 = InitPosePositions[bend.MidNode] - InitPosePositions[bend.End0];
            var toEnd1 = InitPosePositions[bend.MidNode] - InitPosePositions[bend.End1];
            var restHeight = (toEnd0 + toEnd1).Length() / 3f;
            var l0 = toEnd0.Length();
            var l1 = toEnd1.Length();
            if (bend.Height <= MathF.Max(restHeight * 1.0001f, KelagerHeightFloor) || l0 <= 0f || l1 <= 0f)
            {
                return 0f;
            }

            var cosine = ((l0 * l0) + (l1 * l1) - (9f * bend.Height * bend.Height)) / (2f * l0 * l1);
            return float.RadiansToDegrees(MathF.Acos(Math.Clamp(cosine, -1f, 1f)));
        }

        /// <summary>
        /// Gets the <c>add_curvature</c> of a model compiled with <c>rigid_edge_hinges</c>, read off the heights of its
        /// sheet-hub <see cref="KelagerBends"/>; <see cref="SaturatedCurvature"/> when they do not agree.
        /// </summary>
        internal float RigidHingeCurvature
        {
            get
            {
                if (KelagerBends.Count == 0)
                {
                    return 0f;
                }

                var lowest = float.MaxValue;
                var highest = 0f;
                foreach (var bend in KelagerBends)
                {
                    if (HubFold(bend) is not { Shut: false } fold)
                    {
                        continue;
                    }

                    var reading = fold.Reading;
                    lowest = MathF.Min(lowest, reading);
                    highest = MathF.Max(highest, reading);
                }

                if (lowest is float.MaxValue
                    || highest - lowest > ChainRingCurvatureAgreement * MathF.Max(highest, ChainRingCurvatureAgreement)
                    || highest >= 1f - ChainRingCurvatureAgreement)
                {
                    return SaturatedCurvature;
                }

                return Math.Clamp(highest, 0f, 1f);
            }
        }

        internal const float SaturatedCurvature = 1f;

        /// <summary>
        /// Gets the per-hub <c>cloth_bend_stiffness</c> of a rigid-hinged model whose hubs fold by different angles, or
        /// null when <see cref="RigidHingeCurvature"/> accounts for every hub.
        /// </summary>
        internal Dictionary<int, float>? RigidHingeBendPaint
        {
            get
            {
                if (KelagerBends.Count == 0 || RigidHingeCurvature != SaturatedCurvature)
                {
                    return null;
                }

                var lowest = new Dictionary<int, float>();
                var highest = new Dictionary<int, float>();
                var shut = new HashSet<int>();
                foreach (var bend in KelagerBends)
                {
                    if (HubFold(bend) is not { } fold)
                    {
                        continue;
                    }

                    if (fold.Shut)
                    {
                        shut.Add(bend.MidNode);
                        continue;
                    }

                    var reading = fold.Reading;
                    lowest[bend.MidNode] = MathF.Min(lowest.GetValueOrDefault(bend.MidNode, float.MaxValue), reading);
                    highest[bend.MidNode] = MathF.Max(highest.GetValueOrDefault(bend.MidNode), reading);
                }

                var paint = new Dictionary<int, float>();
                foreach (var (hub, high) in highest)
                {
                    if (high - lowest[hub] > ChainRingCurvatureAgreement * MathF.Max(high, ChainRingCurvatureAgreement))
                    {
                        return null;
                    }

                    paint[hub] = high >= 1f - ChainRingCurvatureAgreement ? 1f : Math.Clamp(high, 0f, 1f);
                }

                foreach (var hub in shut)
                {
                    paint.TryAdd(hub, 1f);
                }

                return paint.Values.Any(static value => value > ChainRingCurvatureAgreement) ? paint : null;
            }
        }

        /// <summary>
        /// Reads a sheet-hub bend's fold as a fraction of pi, or marks it shut where its height has fallen to its rest
        /// distance. Null for a bend whose hub is not a generated node or whose arms are degenerate.
        /// </summary>
        private (bool Shut, float Reading)? HubFold(KelagerBend bend)
        {
            if (bend.MidNode < 0 || bend.End0 < 0 || bend.End1 < 0
                || bend.MidNode >= InitPosePositions.Length
                || bend.End0 >= InitPosePositions.Length || bend.End1 >= InitPosePositions.Length
                || bend.MidNode >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[bend.MidNode]))
            {
                return null;
            }

            var toEnd0 = InitPosePositions[bend.End0] - InitPosePositions[bend.MidNode];
            var toEnd1 = InitPosePositions[bend.End1] - InitPosePositions[bend.MidNode];
            var l0 = toEnd0.Length();
            var l1 = toEnd1.Length();
            if (l0 <= 0f || l1 <= 0f)
            {
                return null;
            }

            if (bend.Height <= (toEnd0 + toEnd1).Length() / 3f * 1.0001f)
            {
                return (true, 0f);
            }

            var cosine = ((9f * bend.Height * bend.Height) - (l0 * l0) - (l1 * l1)) / (2f * l0 * l1);
            return (false, MathF.Acos(Math.Clamp(cosine, -1f, 1f)) / MathF.PI);
        }

        /// <summary>
        /// Recovers the per-vertex <c>cloth_bend_stiffness</c> paint of a rigid-hinged proxy sheet, or null when the
        /// sheet's hubs state none. See <see cref="RigidHingeBendPaint"/>.
        /// </summary>
        internal float[]? RecoverRigidHingeBendPaint(ProxyMesh proxy)
        {
            if (RigidHingeBendPaint is not { } byNode)
            {
                return null;
            }

            return PaintPerVertex(proxy, byNode.GetValueOrDefault, static value => value > 0f);
        }

        private const string HingeAnchorPrefix = "$ha_";

        /// <summary>
        /// The hinge constraint a chain joint was authored with. <see cref="Vector"/> spans the joint to
        /// one side of its proxy ring, so its LENGTH is the ring's half-width and overrides the joint's
        /// own extrude radius; the limits are in degrees.
        /// </summary>
        /// <param name="Vector">World-space hinge axis, its length the ring half-width.</param>
        /// <param name="LimitCw">Clockwise angular limit.</param>
        /// <param name="LimitCcw">Counter-clockwise angular limit.</param>
        internal readonly record struct ChainHinge(Vector3 Vector, float LimitCw, float LimitCcw);

        /// <summary>Gets the hinge authored on the joint, or null when it carries none.</summary>
        internal ChainHinge? GetChainHinge(string boneName, int jointNode)
        {
            var ring = ProxyRingOf(jointNode);
            if (ring.Count < 2 || ring[0] >= InitPosePositions.Length || ring[1] >= InitPosePositions.Length)
            {
                return null;
            }

            var limit = HingeLimitOverRing(ring);
            if (limit is null && Array.IndexOf(CtrlNames, HingeAnchorPrefix + boneName) < 0)
            {
                return null;
            }

            var axis = (InitPosePositions[ring[1]] - InitPosePositions[ring[0]]) * 0.5f;
            if (axis.LengthSquared() <= 0f)
            {
                return null;
            }

            axis = BreakHingeFanQuadTie(ring, BreakEndEffectorQuadTie(ring, axis), Quaternion.Identity);

            var (cw, ccw) = limit is { } hinge ? HingeLimitsOf(hinge) : (0f, 0f);
            return new ChainHinge(axis, cw, ccw);
        }

        private KVObject? HingeLimitOverRing(List<int> ring)
        {
            foreach (var hinge in Data.GetArray("m_HingeLimits") ?? [])
            {
                var nodes = hinge.GetIntegerArray("nNode");
                if (nodes.Length >= 2 && (int)nodes[0] == ring[0] && (int)nodes[1] == ring[1])
                {
                    return hinge;
                }
            }

            return null;
        }

        private (float Cw, float Ccw) HingeLimitsOf(KVObject hinge)
        {
            var extents = hinge.GetFloatProperty("flAngleExtents");
            var span = float.RadiansToDegrees(extents) * 2f;
            var rest = HingeRestAngle(hinge);
            if (rest is null)
            {
                return (0f, span);
            }

            var solved = float.RadiansToDegrees(WrapAngle(hinge.GetFloatProperty("flAngleCenter") + extents - rest.Value));
            var cw = Math.Clamp(solved, 0f, span);

            return MathF.Abs(cw - solved) < 0.01f ? (cw, span - cw) : (0f, span);
        }

        private float? HingeRestAngle(KVObject hinge)
        {
            var nodes = hinge.GetIntegerArray("nNode");
            if (nodes.Length < 6 || nodes.Any(node => node < 0 || node >= InitPosePositions.Length))
            {
                return null;
            }

            Vector3 Blend(int a, int b, float weight)
                => Vector3.Lerp(InitPosePositions[a], InitPosePositions[b], weight);

            var origin = InitPosePositions[(int)nodes[0]];
            var axis = Vector3.Normalize(InitPosePositions[(int)nodes[1]] - origin);
            if (!float.IsFinite(axis.X))
            {
                return null;
            }

            Vector3 Perpendicular(Vector3 point)
            {
                var arm = point - origin;
                return Vector3.Normalize(arm - (Vector3.Dot(arm, axis) * axis));
            }

            var reference = Perpendicular(Blend((int)nodes[2], (int)nodes[4], hinge.GetFloatProperty("flWeight4")));
            var arm = Perpendicular(Blend((int)nodes[3], (int)nodes[5], hinge.GetFloatProperty("flWeight5")));
            if (!float.IsFinite(reference.X) || !float.IsFinite(arm.X))
            {
                return null;
            }

            var angle = MathF.Atan2(Vector3.Dot(arm, reference), Vector3.Dot(Vector3.Cross(reference, axis), arm));
            return WrapAngle(angle - (MathF.PI / 2f));
        }

        private static float WrapAngle(float angle)
        {
            var wrapped = angle % MathF.Tau;
            if (wrapped > MathF.PI)
            {
                wrapped -= MathF.Tau;
            }
            else if (wrapped <= -MathF.PI)
            {
                wrapped += MathF.Tau;
            }

            return wrapped;
        }

        private Vector3 BreakEndEffectorQuadTie(List<int> ring, Vector3 axis)
        {
            if (ring.Count < 4 || ring[2] >= InitPosePositions.Length || ring[3] >= InitPosePositions.Length)
            {
                return axis;
            }

            var longest = 0f;
            var shortest = float.MaxValue;
            for (var near = 0; near < 2; near++)
            {
                for (var far = 2; far < 4; far++)
                {
                    var span = Vector3.Distance(InitPosePositions[ring[near]], InitPosePositions[ring[far]]);
                    longest = MathF.Max(longest, span);
                    shortest = MathF.Min(shortest, span);
                }
            }

            if (longest <= 0f || longest - shortest > longest * 1e-5f)
            {
                return axis;
            }

            var tip = InitPosePositions[ring[2]] - InitPosePositions[ring[3]];
            if (tip.LengthSquared() <= 0f)
            {
                return axis;
            }

            return axis + (Vector3.Normalize(tip) * MathF.Max(axis.Length() * 1e-5f, 1e-5f));
        }

        /// <summary>
        /// Tilts a two-node hinge ring's vector along the child quad's diagonals where the quad's four ring-to-ring spans
        /// tie. <paramref name="toVectorFrame"/> maps model space into the vector's frame.
        /// </summary>
        private Vector3 BreakHingeFanQuadTie(List<int> ring, Vector3 vector, Quaternion toVectorFrame)
        {
            if (ring.Count != 2)
            {
                return vector;
            }

            int[]? tied = null;
            foreach (var quad in Quads)
            {
                if (quad.Length != 4 || !((quad[0] == ring[0] && quad[1] == ring[1]) || (quad[0] == ring[1] && quad[1] == ring[0]))
                    || !Array.TrueForAll(quad, corner => corner >= 0 && corner < InitPosePositions.Length))
                {
                    continue;
                }

                var spans = new[]
                {
                    Vector3.Distance(InitPosePositions[quad[0]], InitPosePositions[quad[2]]),
                    Vector3.Distance(InitPosePositions[quad[1]], InitPosePositions[quad[3]]),
                    Vector3.Distance(InitPosePositions[quad[0]], InitPosePositions[quad[3]]),
                    Vector3.Distance(InitPosePositions[quad[1]], InitPosePositions[quad[2]]),
                };
                var longest = spans.Max();
                if (longest <= 0f || longest - spans.Min() > longest * 1e-5f)
                {
                    continue;
                }

                if (tied is not null)
                {
                    return vector;
                }

                tied = quad;
            }

            if (tied is null)
            {
                return vector;
            }

            var across = InitPosePositions[tied[3]] - InitPosePositions[tied[2]];
            if (tied[0] == ring[0])
            {
                across = -across;
            }

            if (across.LengthSquared() <= 0f)
            {
                return vector;
            }

            var direction = Vector3.Normalize(Vector3.Transform(across, toVectorFrame));
            return vector + (direction * MathF.Max(vector.Length() * 1e-5f, 1e-5f));
        }

        /// <summary>Gets whether a chain joint carries a hinge constraint.</summary>
        internal bool IsHingedJoint(int jointNode)
        {
            var ring = ProxyRingOf(jointNode);
            return ring.Count >= 2 && HingeLimitOverRing(ring) is not null;
        }

        private bool IsHingeRegeneratedProxy(int node)
        {
            if (node >= CtrlNames.Length || !IsProxyNodeName(CtrlNames[node]))
            {
                return false;
            }

            var parent = node < SkelParents.Length ? SkelParents[node] : -1;
            if (parent < 0 || parent >= CtrlNames.Length)
            {
                return false;
            }

            if (Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[parent]) >= 0)
            {
                return true;
            }

            return IsHingedJoint(parent) || RigidHingeJoints.ContainsKey(parent);
        }

        /// <summary>
        /// Gets whether a surface element joins a hinged joint of <paramref name="chain"/> to one of its children.
        /// </summary>
        internal bool HasRigidHingeLink(BoneChain chain)
        {
            var groupOf = ChainNodeGroups(chain);

            var hingedLinks = chain.Joints
                .Where(joint => !joint.IsRoot && IsHingedJoint(joint.ParentNode))
                .Select(static joint => (joint.ParentNode, joint.Node))
                .ToList();

            bool JoinsAHingedLink(int[] face, bool overTheRing)
            {
                var groups = new HashSet<int>();
                foreach (var node in face)
                {
                    if (groupOf.TryGetValue(node, out var group))
                    {
                        groups.Add(group);
                    }
                }

                return hingedLinks.Exists(link => groups.Contains(link.ParentNode) && groups.Contains(link.Node)
                    && (!overTheRing || (ProxyRingOf(link.ParentNode) is { Count: 2 } ring && SpansRing(face, ring))));
            }

            foreach (var quad in Quads)
            {
                if (JoinsAHingedLink(quad, overTheRing: false))
                {
                    return true;
                }
            }

            foreach (var tri in Tris)
            {
                if (JoinsAHingedLink(tri, overTheRing: true))
                {
                    return true;
                }
            }

            return false;
        }

        private Dictionary<int, Vector3>? rigidHingeJoints;

        private Dictionary<int, Vector3>? hingeFanJoints;

        /// <summary>
        /// Gets the chain joints a rigid <c>ClothChainHinge</c> constrains, with each <c>hinge_vector</c> in the joint's bone
        /// frame. Hinges with limits or an <c>$ha_</c> anchor are not listed.
        /// </summary>
        internal IReadOnlyDictionary<int, Vector3> RigidHingeJoints => rigidHingeJoints ??= CollectHingeFanJoints(rigidOnly: true);

        /// <summary>
        /// Gets every chain joint whose hinge the compiler fanned out over surface elements, limited and anchored hinges included.
        /// </summary>
        private IReadOnlyDictionary<int, Vector3> HingeFanJoints => hingeFanJoints ??= CollectHingeFanJoints(rigidOnly: false);

        /// <summary>
        /// Gets whether a compiled quad or triangle is one element of a chain hinge's fan: no sheet vertex
        /// among its corners, and the ring pair of a hinged joint across them.
        /// </summary>
        internal bool IsHingeFanFace(int[] face)
        {
            if (HingeFanJoints.Count == 0 || !IsChainOnlyFace(face))
            {
                return false;
            }

            foreach (var joint in HingeFanJoints.Keys)
            {
                var ring = ProxyRingOf(joint);
                if (ring.Count == 2 && SpansRing(face, ring))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Gets whether a rod joins the rings of two different children of a rigid hinge joint (<c>child_sibling_spring</c>).
        /// </summary>
        internal bool SpringsHingeChildren(BoneChain chain, int joint)
        {
            if (!RigidHingeJoints.ContainsKey(joint))
            {
                return false;
            }

            var childOfRing = new Dictionary<int, int>();
            var children = 0;
            foreach (var child in chain.Joints)
            {
                if (child.ParentNode != joint)
                {
                    continue;
                }

                children++;
                foreach (var ring in ProxyRingOf(child.Node))
                {
                    childOfRing[ring] = child.Node;
                }
            }

            if (children < 2)
            {
                return false;
            }

            foreach (var rod in Rods)
            {
                if (childOfRing.TryGetValue(rod.NodeA, out var a)
                    && childOfRing.TryGetValue(rod.NodeB, out var b) && a != b)
                {
                    return true;
                }
            }

            return false;
        }

        private bool IsChainOnlyFace(int[] face)
            => !Array.Exists(face, corner => corner < 0 || corner >= CtrlNames.Length || IsProxyMeshNode(corner));

        private static bool SpansRing(int[] face, List<int> ring)
            => Array.IndexOf(face, ring[0]) >= 0 && Array.IndexOf(face, ring[1]) >= 0;

        private Dictionary<int, Vector3> CollectHingeFanJoints(bool rigidOnly)
        {
            var joints = new Dictionary<int, Vector3>();
            foreach (var face in Quads.Concat(Tris))
            {
                if (!IsChainOnlyFace(face))
                {
                    continue;
                }

                foreach (var corner in face)
                {
                    var joint = corner < SkelParents.Length ? SkelParents[corner] : -1;
                    if (joint < 0 || joints.ContainsKey(joint)
                        || !CtrlNames[corner].StartsWith("$cc", StringComparison.Ordinal))
                    {
                        continue;
                    }

                    var ring = ProxyRingOf(joint);
                    if (ring.Count != 2 || !SpansRing(face, ring)
                        || !Array.TrueForAll(face, other => ring.Contains(other)
                            || (ChainJointOf(other) is { } child && ParentJointOf(child) == joint))
                        || (rigidOnly && HingeLimitOverRing(ring) is not null)
                        || (rigidOnly && Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[joint]) >= 0)
                        || RigidHingeVector(joint, ring) is not { } vector)
                    {
                        continue;
                    }

                    var toBoneFrame = joint < InitPoseRotations.Length ? Quaternion.Inverse(InitPoseRotations[joint]) : Quaternion.Identity;
                    joints[joint] = BreakHingeFanQuadTie(ring, vector, toBoneFrame);
                }
            }

            return joints;
        }

        private int? ChainJointOf(int node)
        {
            if (!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal))
            {
                return IsGeneratedNodeName(CtrlNames[node]) ? null : node;
            }

            var owner = node < SkelParents.Length ? SkelParents[node] : -1;
            return owner >= 0 ? owner : null;
        }

        private int ParentJointOf(int joint) => joint < SkelParents.Length ? SkelParents[joint] : -1;

        private Vector3? RigidHingeVector(int joint, List<int> ring)
        {
            foreach (var offset in CtrlOffsets)
            {
                if (offset.CtrlChild == ring[1] && offset.CtrlParent == joint)
                {
                    return offset.Offset;
                }
            }

            if (ring[1] >= InitPosePositions.Length || joint >= InitPoseRotations.Length)
            {
                return null;
            }

            var half = (InitPosePositions[ring[1]] - InitPosePositions[ring[0]]) * 0.5f;
            return Vector3.Transform(half, Quaternion.Inverse(InitPoseRotations[joint]));
        }
    }
}

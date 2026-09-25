using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
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

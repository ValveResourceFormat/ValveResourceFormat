using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        Dictionary<int, Vector3>? rigidHingeJoints;

        /// <summary>
        /// Gets the chain joints a rigid <c>ClothChainHinge</c> constrains, each with its authored
        /// <c>hinge_vector</c> in the joint's own bone frame: the compiler lays the joint's two-node ring at
        /// the joint plus and minus that vector, then joins the pair to every child joint with one surface
        /// element per child (a quad where the child has a ring, a triangle where it has none). A soft hinge
        /// or one with limits leaves an <c>$ha_</c> anchor or an <c>m_HingeLimits</c> entry behind instead
        /// and is not listed here.
        /// </summary>
        public IReadOnlyDictionary<int, Vector3> RigidHingeJoints => rigidHingeJoints ??= BuildRigidHingeJoints();

        /// <summary>
        /// Gets whether a compiled quad or triangle is one element of a rigid hinge's fan: no sheet vertex
        /// among its corners, and the ring pair of a <see cref="RigidHingeJoints"/> joint across them.
        /// </summary>
        public bool IsRigidHingeFace(int[] face)
        {
            if (RigidHingeJoints.Count == 0 || !IsChainOnlyFace(face))
            {
                return false;
            }

            foreach (var joint in RigidHingeJoints.Keys)
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
        /// Gets whether a rigid hinge joint's own children were sprung against each other
        /// (<c>child_sibling_spring</c>). The compiler then rods every pair of their extruded rings, which
        /// neither the hinge's fan elements nor the chain's parent-child rods produce, so a rod joining the
        /// rings of two different children of the joint is the signature.
        /// </summary>
        public bool SpringsHingeChildren(BoneChain chain, int joint)
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

        // A face the sheet cannot own: every corner is in range and none of them is a sheet vertex.
        bool IsChainOnlyFace(int[] face)
            => !Array.Exists(face, corner => corner < 0 || corner >= CtrlNames.Length || IsProxyMeshNode(corner));

        static bool SpansRing(int[] face, List<int> ring)
            => Array.IndexOf(face, ring[0]) >= 0 && Array.IndexOf(face, ring[1]) >= 0;

        Dictionary<int, Vector3> BuildRigidHingeJoints()
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

                    // The fan runs from the hinge ring to one child joint per element, so every other
                    // corner is that child or a node of its ring.
                    var ring = ProxyRingOf(joint);
                    if (ring.Count != 2 || !SpansRing(face, ring)
                        || !Array.TrueForAll(face, other => ring.Contains(other)
                            || (ChainJointOf(other) is { } child && ParentJointOf(child) == joint))
                        || HingeLimitOverRing(ring) is not null
                        || Array.IndexOf(CtrlNames, HingeAnchorPrefix + CtrlNames[joint]) >= 0
                        || RigidHingeVector(joint, ring) is not { } vector)
                    {
                        continue;
                    }

                    joints[joint] = vector;
                }
            }

            return joints;
        }

        // The chain joint a fan corner stands for: a ring node's owner, otherwise the node itself.
        int? ChainJointOf(int node)
        {
            if (!CtrlNames[node].StartsWith("$cc", StringComparison.Ordinal))
            {
                return IsGeneratedNodeName(CtrlNames[node]) ? null : node;
            }

            var owner = node < SkelParents.Length ? SkelParents[node] : -1;
            return owner >= 0 ? owner : null;
        }

        int ParentJointOf(int joint) => joint < SkelParents.Length ? SkelParents[joint] : -1;

        // The second ring node's bone-local offset is the vector as authored; without an offset entry the
        // half-span between the pair is taken back into the joint's bind frame.
        Vector3? RigidHingeVector(int joint, List<int> ring)
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

using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>A cloth collision capsule recovered from <c>m_TaperedCapsuleRigids</c>.</summary>
        public sealed class CollisionCapsule
        {
            /// <summary>Gets the bone the capsule is attached to (its node resolved to a real skeleton bone).</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the first end-cap centre (bone-local).</summary>
            public required Vector3 Point0 { get; init; }
            /// <summary>Gets the first end-cap radius.</summary>
            public required float Radius0 { get; init; }
            /// <summary>Gets the second end-cap centre (bone-local).</summary>
            public required Vector3 Point1 { get; init; }
            /// <summary>Gets the second end-cap radius.</summary>
            public required float Radius1 { get; init; }
            /// <summary>Gets the 4-bit collision-layer mask.</summary>
            public int CollisionMask { get; init; }
            /// <summary>
            /// Gets the vertex map scoping which cloth vertices this capsule collides with, or null when it
            /// collides with everything. A scoped capsule compiles with an all-ones
            /// <c>nCollisionMask</c> in place of its authored layer bits.
            /// </summary>
            public string? VertexMap { get; init; }
            /// <summary>
            /// Gets whether the shape keeps the cloth INSIDE it rather than out of it.
            /// </summary>
            public bool Inverted { get; init; }
            /// <summary>
            /// Gets whether the capsule collides as a per-node plane rather than as a volume. Such a capsule
            /// leaves no rigid of its own behind, only <c>m_CollisionPlanes</c>.
            /// </summary>
            public bool Planarize { get; init; }
            /// <summary>Gets the authored collision priority, recovered by <see cref="ColliderPriority"/>.</summary>
            public int Priority { get; init; }
        }

        /// <summary>A cloth collision box recovered from <c>m_BoxRigids</c>.</summary>
        public sealed class CollisionBox
        {
            /// <summary>Gets the bone the box is attached to.</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the box centre (bone-local).</summary>
            public required Vector3 Origin { get; init; }
            /// <summary>Gets the box orientation (bone-local).</summary>
            public required Quaternion Rotation { get; init; }
            /// <summary>Gets the box half-extents.</summary>
            public required Vector3 Size { get; init; }
            /// <summary>Gets the 4-bit collision-layer mask.</summary>
            public int CollisionMask { get; init; }
            /// <summary>
            /// Gets the vertex map scoping which cloth vertices this box collides with, or null when it
            /// collides with everything. A scoped box compiles with an all-ones <c>nCollisionMask</c> in
            /// place of its authored layer bits.
            /// </summary>
            public string? VertexMap { get; init; }
            /// <summary>
            /// Gets whether the shape keeps the cloth INSIDE it rather than out of it.
            /// </summary>
            public bool Inverted { get; init; }
            /// <summary>Gets the authored collision priority, recovered by <see cref="ColliderPriority"/>.</summary>
            public int Priority { get; init; }
        }

        /// <summary>A cloth collision sphere recovered from <c>m_SphereRigids</c>.</summary>
        public sealed class CollisionSphere
        {
            /// <summary>Gets the bone the sphere is attached to.</summary>
            public required string? ParentBone { get; init; }
            /// <summary>Gets the sphere centre (bone-local).</summary>
            public required Vector3 Center { get; init; }
            /// <summary>Gets the sphere radius.</summary>
            public required float Radius { get; init; }
            /// <summary>Gets the 4-bit collision-layer mask.</summary>
            public int CollisionMask { get; init; }
            /// <summary>
            /// Gets the vertex map scoping which cloth vertices this sphere collides with, or null when it
            /// collides with everything. A scoped sphere compiles with an all-ones <c>nCollisionMask</c> in
            /// place of its authored layer bits.
            /// </summary>
            public string? VertexMap { get; init; }
            /// <summary>
            /// Gets whether the shape keeps the cloth INSIDE it rather than out of it.
            /// </summary>
            public bool Inverted { get; init; }
            /// <summary>Gets the authored collision priority, recovered by <see cref="ColliderPriority"/>.</summary>
            public int Priority { get; init; }
        }

        /// <summary>Which per-type array of <c>m_RigidColliderPriorities</c> a collider is indexed by.</summary>
        enum RigidColliderKind
        {
            TaperedCapsule,
            Sphere,
            Box,
            CollisionPlane,
        }

        /// <summary>
        /// Gets the collision priority of the collider at <paramref name="index"/> in its own compiled array.
        /// <para>
        /// The compiler sorts each collider array by the authored priority and emits
        /// <c>m_RigidColliderPriorities</c> row <c>g</c> as the first index of group <c>g</c>, so a collider
        /// belongs to the last group whose start index it reaches. Groups are ranked, not the authored
        /// integers: one group per distinct authored value, ascending, and no array at all when a model uses
        /// a single value. Re-authoring the rank therefore reproduces the compiled array exactly.
        /// </para>
        /// </summary>
        int ColliderPriority(RigidColliderKind kind, int index)
        {
            var priority = 0;

            for (var group = 1; group < RigidColliderPriorities.Length; group++)
            {
                var row = RigidColliderPriorities[group];
                var start = kind switch
                {
                    RigidColliderKind.TaperedCapsule => row.TaperedCapsuleRigidIndex,
                    RigidColliderKind.Sphere => row.SphereRigidIndex,
                    RigidColliderKind.Box => row.BoxRigidIndex,
                    _ => row.CollisionPlaneIndex,
                };

                if (index < start)
                {
                    break;
                }

                priority = group;
            }

            return priority;
        }

        // Resolves a rigid's nNode to its bone name. Collision rigids are anchored to a real bone; if the
        // node happens to be an auto-generated proxy node, walk up to the first real bone.
        string? ResolveRigidBone(int node)
        {
            if (node < 0 || node >= CtrlNames.Length)
            {
                return null;
            }

            return IsProxyNodeName(CtrlNames[node]) ? ResolveSkinBone(node) : CtrlNames[node];
        }

        // A rigid's nVertexMapIndex resolved to the selection it scopes the collider to. An unscoped rigid
        // writes an out-of-range index, and the oldest compiles write no index at all.
        string? RigidVertexMap(int index)
            => index >= 0 && index < VertexMaps.Count ? VertexMaps[index].Name : null;
    }
}

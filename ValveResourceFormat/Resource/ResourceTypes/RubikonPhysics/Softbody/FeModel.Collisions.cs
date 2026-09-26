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
            /// <summary>Gets the vertex map scoping which cloth vertices the shape collides with, or null for all of them.</summary>
            public string? VertexMap { get; init; }
            /// <summary>Gets whether the shape keeps the cloth inside it rather than out of it.</summary>
            public bool Inverted { get; init; }
            /// <summary>Gets whether the shape collides as per-node planes (<c>m_CollisionPlanes</c>) rather than as a volume.</summary>
            public bool Planarize { get; init; }

            /// <summary>
            /// Gets how many <c>m_CollisionPlanes</c> entries the fit this planarized shape came from owns.
            /// </summary>
            internal int PlanarizePlanes { get; init; }

            /// <summary>Gets how many of <see cref="PlanarizePlanes"/> fall in this copy's own selection.</summary>
            internal int PlanarizeOwnPlanes { get; init; }
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
            /// <summary>Gets the vertex map scoping which cloth vertices the shape collides with, or null for all of them.</summary>
            public string? VertexMap { get; init; }
            /// <summary>Gets whether the shape keeps the cloth inside it rather than out of it.</summary>
            public bool Inverted { get; init; }
            /// <summary>Gets how many <c>m_CollisionPlanes</c> entries this planarized box owns.</summary>
            internal int PlanarizePlanes { get; init; }

            /// <summary>Gets how many of <see cref="PlanarizePlanes"/> fall in this box's own selection.</summary>
            internal int PlanarizeOwnPlanes { get; init; }
            /// <summary>Gets whether the shape collides as per-node planes (<c>m_CollisionPlanes</c>) rather than as a volume.</summary>
            public bool Planarize { get; init; }
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
            /// <summary>Gets the vertex map scoping which cloth vertices the shape collides with, or null for all of them.</summary>
            public string? VertexMap { get; init; }
            /// <summary>Gets whether the shape keeps the cloth inside it rather than out of it.</summary>
            public bool Inverted { get; init; }
            /// <summary>Gets the authored collision priority, recovered by <see cref="ColliderPriority"/>.</summary>
            public int Priority { get; init; }
        }

        /// <summary>Which per-type array of <c>m_RigidColliderPriorities</c> a collider is indexed by.</summary>
        private enum RigidColliderKind
        {
            TaperedCapsule,
            Sphere,
            Box,
            CollisionPlane,
        }

        /// <summary>
        /// Gets the rank of the <c>m_RigidColliderPriorities</c> group the collider at <paramref name="index"/> of its own
        /// array falls in.
        /// </summary>
        private int ColliderPriority(RigidColliderKind kind, int index)
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

        /// <summary>Gets the bone a rigid's node resolves to, following a proxy node up to its skin bone.</summary>
        private string? ResolveRigidBone(int node)
        {
            if (node < 0 || node >= CtrlNames.Length)
            {
                return null;
            }

            return IsProxyNodeName(CtrlNames[node]) ? ResolveSkinBone(node) : CtrlNames[node];
        }

        private string? RigidVertexMap(int index)
            => index >= 0 && index < VertexMaps.Count ? VertexMaps[index].Name : null;

        private const uint RigidFlagInverted = 1;

        /// <summary>The collision-layer mask a planarized shape is recovered with: all four layers.</summary>
        private const int PlanarizeCollisionMask = 0xF;

        /// <summary>Reconstructs the cloth collision capsules (<c>m_TaperedCapsuleRigids</c>).</summary>
        public List<CollisionCapsule> BuildCollisionCapsules()
        {
            var result = new List<CollisionCapsule>();
            var rigids = Data.GetArray("m_TaperedCapsuleRigids");
            if (rigids is null)
            {
                return result;
            }

            for (var i = 0; i < rigids.Count; i++)
            {
                var rigid = rigids[i];
                Vector3 point0;
                Vector3 point1;
                float radius0;
                float radius1;

                if (rigid.GetArray("vSphere") is { Count: >= 2 } spheres)
                {
                    var s0 = spheres[0].ToVector4();
                    var s1 = spheres[1].ToVector4();
                    point0 = new Vector3(s0.X, s0.Y, s0.Z);
                    radius0 = s0.W;
                    point1 = new Vector3(s1.X, s1.Y, s1.Z);
                    radius1 = s1.W;
                }
                else if (rigid.GetArray("vCenter") is { Count: >= 2 } centres
                    && rigid.GetArray<float>("flRadius") is { Length: >= 2 } radii)
                {
                    point0 = centres[0].ToVector3();
                    point1 = centres[1].ToVector3();
                    radius0 = radii[0];
                    radius1 = radii[1];
                }
                else
                {
                    continue;
                }

                var node = rigid.GetInt32Property("nNode");

                result.Add(new CollisionCapsule
                {
                    ParentBone = ResolveRigidBone(node),
                    Point0 = point0,
                    Radius0 = radius0,
                    Point1 = point1,
                    Radius1 = radius1,
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                    VertexMap = RigidVertexMap(rigid.GetInt32Property("nVertexMapIndex", -1)),
                    Inverted = (rigid.GetUInt32Property("nFlags") & RigidFlagInverted) != 0,
                    Priority = ColliderPriority(RigidColliderKind.TaperedCapsule, i),
                });
            }

            return result;
        }

        /// <summary>
        /// Reconstructs the collision capsules authored with <c>planarize</c> from their <c>m_CollisionPlanes</c>,
        /// keeping only a group of planes the recovered capsules reproduce in full.
        /// </summary>
        internal List<CollisionCapsule> BuildPlanarizeCapsules()
        {
            var result = new List<CollisionCapsule>();
            if (CollisionPlanes.Length == 0 || InitPosePositions.Length == 0)
            {
                return result;
            }

            var indexed = CollisionPlanes.Select(static (plane, index) => (plane, index));
            foreach (var group in indexed.GroupBy(static e => e.plane.CtrlParent))
            {
                var parent = group.Key;
                if (parent < 0 || parent >= InitPosePositions.Length)
                {
                    continue;
                }

                var samples = PlanarizeSamples(parent, group.Select(static e => e.index));
                if (PlanarizedShapeFitter.FitPlanarizedShapes(samples) is not { } shapes)
                {
                    continue;
                }

                var priority = ColliderPriority(RigidColliderKind.CollisionPlane,
                    group.Min(static e => e.index));
                var bone = ResolveRigidBone(parent);
                var recovered = new List<CollisionCapsule>(shapes.Count);

                foreach (var (fit, members) in shapes)
                {
                    var (point0, point1) = PlanarizedShapeFitter.PlanarizedAxis(fit, samples, members);
                    CollisionCapsule Capsule(string vertexMap, int ownPlanes) => new()
                    {
                        ParentBone = bone,
                        Point0 = point0,
                        Radius0 = fit.R0,
                        Point1 = point1,
                        Radius1 = fit.R1,
                        CollisionMask = PlanarizeCollisionMask,
                        VertexMap = vertexMap,
                        Planarize = true,
                        PlanarizePlanes = members.Count,
                        PlanarizeOwnPlanes = ownPlanes,
                        Priority = priority,
                    };

                    if (SmallestVertexMapCovering(samples, members) is { } vertexMap)
                    {
                        recovered.Add(Capsule(vertexMap, members.Count));
                        continue;
                    }

                    if (SplitByVertexMap(samples, members) is not { } split)
                    {
                        recovered.Clear();
                        break;
                    }

                    foreach (var (splitMap, splitMembers) in split)
                    {
                        recovered.Add(Capsule(splitMap, splitMembers.Count));
                    }
                }

                result.AddRange(recovered);
            }

            return result;
        }

        /// <summary>
        /// Reconstructs the collision boxes authored with <c>planarize</c> from the plane groups no planarized capsule
        /// explains, keeping only a box that reproduces every plane it owns.
        /// </summary>
        internal List<CollisionBox> BuildPlanarizeBoxes()
        {
            var result = new List<CollisionBox>();
            if (CollisionPlanes.Length == 0 || InitPosePositions.Length == 0)
            {
                return result;
            }

            var indexed = CollisionPlanes.Select(static (plane, index) => (plane, index));
            foreach (var group in indexed.GroupBy(static e => e.plane.CtrlParent))
            {
                var parent = group.Key;
                if (parent < 0 || parent >= InitPosePositions.Length)
                {
                    continue;
                }

                var samples = PlanarizeSamples(parent, group.Select(static e => e.index));
                if (PlanarizedShapeFitter.FitPlanarizedShapes(samples) is not null || PlanarizedShapeFitter.FitOrientedPlanarizedBox(samples) is not { } box
                    || SmallestVertexMapCovering(samples, [.. Enumerable.Range(0, samples.Count)]) is not { } vertexMap)
                {
                    continue;
                }

                result.Add(new CollisionBox
                {
                    ParentBone = ResolveRigidBone(parent),
                    Origin = Vector3.Transform((box.Min + box.Max) * 0.5f, box.Rotation),
                    Rotation = box.Rotation,
                    Size = (box.Max - box.Min) * 0.5f,
                    CollisionMask = PlanarizeCollisionMask,
                    VertexMap = vertexMap,
                    Planarize = true,
                    PlanarizePlanes = samples.Count,
                    PlanarizeOwnPlanes = samples.Count,
                    Priority = ColliderPriority(RigidColliderKind.CollisionPlane, group.Min(static e => e.index)),
                });
            }

            return result;
        }

        private List<PlanarizeSample> PlanarizeSamples(int parent, IEnumerable<int> planeIndices)
        {
            var toLocal = Quaternion.Conjugate(InitPoseRotations[parent]);
            var origin = InitPosePositions[parent];

            var samples = new List<PlanarizeSample>();
            foreach (var index in planeIndices)
            {
                var plane = CollisionPlanes[index];
                var node = plane.ChildNode;
                if (node < 0 || node >= InitPosePositions.Length)
                {
                    continue;
                }

                var x = Vector3.Transform(InitPosePositions[node] - origin, toLocal);
                var normal = plane.PlaneNormal;
                samples.Add(new PlanarizeSample(node, x, normal, plane.PlaneOffset,
                    Vector3.Dot(normal, x) - plane.PlaneOffset, GetCollisionRadius(node)));
            }

            return samples;
        }

        private string? SmallestVertexMapCovering(List<PlanarizeSample> samples, List<int> members)
        {
            var owned = members.Select(i => samples[i].Node).ToHashSet();
            string? name = null;
            var smallest = int.MaxValue;

            foreach (var map in VertexMaps)
            {
                if (!owned.All(node => map.WeightOf(node) > 0f))
                {
                    continue;
                }

                var size = map.Weights.Count(static w => w > 0f);
                if (size < smallest)
                {
                    smallest = size;
                    name = map.Name;
                }
            }

            return name;
        }

        /// <summary>
        /// Groups a shape's planes by the smallest selection covering each plane's node, or null when a node has none.
        /// </summary>
        private List<(string Map, List<int> Members)>? SplitByVertexMap(List<PlanarizeSample> samples, List<int> members)
        {
            var groups = new Dictionary<string, List<int>>();
            foreach (var member in members)
            {
                if (SmallestVertexMapCovering(samples, [member]) is not { } map)
                {
                    return null;
                }

                if (!groups.TryGetValue(map, out var group))
                {
                    groups[map] = group = [];
                }

                group.Add(member);
            }

            return [.. groups.Select(static kv => (kv.Key, kv.Value))];
        }

        /// <summary>Reconstructs the cloth collision boxes (<c>m_BoxRigids</c>).</summary>
        public List<CollisionBox> BuildCollisionBoxes()
        {
            var result = new List<CollisionBox>();
            var rigids = Data.GetArray("m_BoxRigids");
            if (rigids is null)
            {
                return result;
            }

            for (var i = 0; i < rigids.Count; i++)
            {
                var rigid = rigids[i];
                Vector3 origin;
                Quaternion rotation;

                if (rigid.GetSubCollection("tmFrame2") is { } frame)
                {
                    (origin, _, rotation) = frame.ToTransform();
                }
                else if (rigid.GetSubCollection("tmFrame") is { } matrixFrame)
                {
                    var matrix = matrixFrame.ToMatrix4x4();
                    origin = matrix.Translation;
                    rotation = Quaternion.CreateFromRotationMatrix(matrix);
                }
                else
                {
                    continue;
                }

                var node = rigid.GetInt32Property("nNode");

                result.Add(new CollisionBox
                {
                    ParentBone = ResolveRigidBone(node),
                    Origin = origin,
                    Rotation = rotation,
                    Size = rigid.GetSubCollection("vSize").ToVector3(),
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                    VertexMap = RigidVertexMap(rigid.GetInt32Property("nVertexMapIndex", -1)),
                    Inverted = (rigid.GetUInt32Property("nFlags") & RigidFlagInverted) != 0,
                    Priority = ColliderPriority(RigidColliderKind.Box, i),
                });
            }

            return result;
        }

        /// <summary>Reconstructs the cloth collision spheres (<c>m_SphereRigids</c>).</summary>
        public List<CollisionSphere> BuildCollisionSpheres()
        {
            var result = new List<CollisionSphere>();
            var rigids = Data.GetArray("m_SphereRigids");
            if (rigids is null)
            {
                return result;
            }

            for (var i = 0; i < rigids.Count; i++)
            {
                var rigid = rigids[i];

                Vector4 sphere;
                if (rigid.GetArray<float>("vSphere") is { Length: 4 } s)
                {
                    sphere = new Vector4(s[0], s[1], s[2], s[3]);
                }
                else if (rigid.ContainsKey("m_vSphere"))
                {
                    sphere = rigid.GetSubCollection("m_vSphere").ToVector4();
                }
                else if (rigid.GetSubCollection("vCenter") is { } centre)
                {
                    sphere = new Vector4(centre.ToVector3(), rigid.GetFloatProperty("flRadius"));
                }
                else
                {
                    continue;
                }

                var node = rigid.GetInt32Property("nNode");
                result.Add(new CollisionSphere
                {
                    ParentBone = ResolveRigidBone(node),
                    Center = new Vector3(sphere.X, sphere.Y, sphere.Z),
                    Radius = sphere.W,
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                    VertexMap = RigidVertexMap(rigid.GetInt32Property("nVertexMapIndex", -1)),
                    Inverted = (rigid.GetUInt32Property("nFlags") & RigidFlagInverted) != 0,
                    Priority = ColliderPriority(RigidColliderKind.Sphere, i),
                });
            }

            return result;
        }
    }
}

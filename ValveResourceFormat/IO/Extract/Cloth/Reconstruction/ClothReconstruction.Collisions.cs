using System.Linq;
using ValveKeyValue;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody.FeModel;

namespace ValveResourceFormat.IO
{
    /// <summary>The properties every recovered cloth collision shape carries.</summary>
    internal abstract class CollisionShape
    {
        /// <summary>Gets the bone the shape is attached to (its node resolved to a real skeleton bone).</summary>
        public string? ParentBone { get; internal set; }
        /// <summary>Gets the 4-bit collision-layer mask.</summary>
        public int CollisionMask { get; internal set; }
        /// <summary>Gets the vertex map scoping which cloth vertices the shape collides with, or null for all of them.</summary>
        public string? VertexMap { get; internal set; }
        /// <summary>Gets whether the shape keeps the cloth inside it rather than out of it.</summary>
        public bool Inverted { get; internal set; }
        /// <summary>Gets the authored collision priority, recovered by <see cref="ClothReconstruction.ColliderPriority"/>.</summary>
        public int Priority { get; internal set; }
    }

    /// <summary>A cloth collision shape that may have been compiled into per-node planes.</summary>
    internal abstract class PlanarizableCollisionShape : CollisionShape
    {
        /// <summary>Gets whether the shape collides as per-node planes (<c>m_CollisionPlanes</c>) rather than as a volume.</summary>
        public bool Planarize { get; init; }
        /// <summary>Gets how many <c>m_CollisionPlanes</c> entries the fit this planarized shape came from owns.</summary>
        internal int PlanarizePlanes { get; init; }
        /// <summary>Gets how many of <see cref="PlanarizePlanes"/> fall in this shape's own selection.</summary>
        internal int PlanarizeOwnPlanes { get; init; }
    }

    /// <summary>A cloth collision capsule recovered from <c>m_TaperedCapsuleRigids</c>.</summary>
    internal sealed class CollisionCapsule : PlanarizableCollisionShape
    {
        /// <summary>Gets the first end-cap centre (bone-local).</summary>
        public required Vector3 Point0 { get; init; }
        /// <summary>Gets the first end-cap radius.</summary>
        public required float Radius0 { get; init; }
        /// <summary>Gets the second end-cap centre (bone-local).</summary>
        public required Vector3 Point1 { get; init; }
        /// <summary>Gets the second end-cap radius.</summary>
        public required float Radius1 { get; init; }
    }

    /// <summary>A cloth collision box recovered from <c>m_BoxRigids</c>.</summary>
    internal sealed class CollisionBox : PlanarizableCollisionShape
    {
        /// <summary>Gets the box centre (bone-local).</summary>
        public required Vector3 Origin { get; init; }
        /// <summary>Gets the box orientation (bone-local).</summary>
        public required Quaternion Rotation { get; init; }
        /// <summary>Gets the box half-extents.</summary>
        public required Vector3 Size { get; init; }
    }

    /// <summary>A cloth collision sphere recovered from <c>m_SphereRigids</c>.</summary>
    internal sealed class CollisionSphere : CollisionShape
    {
        /// <summary>Gets the sphere centre (bone-local).</summary>
        public required Vector3 Center { get; init; }
        /// <summary>Gets the sphere radius.</summary>
        public required float Radius { get; init; }
    }

    internal sealed partial class ClothReconstruction
    {
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

            for (var group = 1; group < Fe.RigidColliderPriorities.Length; group++)
            {
                var row = Fe.RigidColliderPriorities[group];
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
            if (node < 0 || node >= Fe.CtrlNames.Length)
            {
                return null;
            }

            return IsProxyNodeName(Fe.CtrlNames[node]) ? ResolveSkinBone(node) : Fe.CtrlNames[node];
        }

        private string? RigidVertexMap(int index)
            => index >= 0 && index < VertexMaps.Count ? VertexMaps[index].Name : null;

        private const uint RigidFlagInverted = 1;

        /// <summary>The collision-layer mask a planarized shape is recovered with: all four layers.</summary>
        private const int PlanarizeCollisionMask = 0xF;

        /// <summary>Reconstructs the cloth collision capsules (<c>m_TaperedCapsuleRigids</c>).</summary>
        public List<CollisionCapsule> BuildCollisionCapsules()
            => ReadRigids("m_TaperedCapsuleRigids", RigidColliderKind.TaperedCapsule, static rigid =>
            {
                if (rigid.GetArray("vSphere") is { Count: >= 2 } spheres)
                {
                    var s0 = spheres[0].ToVector4();
                    var s1 = spheres[1].ToVector4();
                    return new CollisionCapsule
                    {
                        Point0 = new Vector3(s0.X, s0.Y, s0.Z),
                        Radius0 = s0.W,
                        Point1 = new Vector3(s1.X, s1.Y, s1.Z),
                        Radius1 = s1.W,
                    };
                }

                return rigid.GetArray("vCenter") is { Count: >= 2 } centres && rigid.GetArray<float>("flRadius") is { Length: >= 2 } radii
                    ? new CollisionCapsule { Point0 = centres[0].ToVector3(), Radius0 = radii[0], Point1 = centres[1].ToVector3(), Radius1 = radii[1] }
                    : null;
            });

        /// <summary>
        /// Reads every entry of the rigid array <paramref name="key"/> whose shape <paramref name="read"/> recognises, and gives
        /// it the properties all rigids share.
        /// </summary>
        private List<TShape> ReadRigids<TShape>(string key, RigidColliderKind kind, Func<KVObject, TShape?> read)
            where TShape : CollisionShape
        {
            var result = new List<TShape>();
            var rigids = Fe.Data.GetArray(key) ?? [];
            for (var i = 0; i < rigids.Count; i++)
            {
                var rigid = rigids[i];
                if (read(rigid) is not { } shape)
                {
                    continue;
                }

                shape.ParentBone = ResolveRigidBone(rigid.GetInt32Property("nNode"));
                shape.CollisionMask = rigid.GetInt32Property("nCollisionMask");
                shape.VertexMap = RigidVertexMap(rigid.GetInt32Property("nVertexMapIndex", -1));
                shape.Inverted = (rigid.GetUInt32Property("nFlags") & RigidFlagInverted) != 0;
                shape.Priority = ColliderPriority(kind, i);
                result.Add(shape);
            }

            return result;
        }

        /// <summary>
        /// Reconstructs the collision capsules authored with <c>planarize</c> from their <c>m_CollisionPlanes</c>,
        /// keeping only a group of planes the recovered capsules reproduce in full.
        /// </summary>
        internal List<CollisionCapsule> BuildPlanarizeCapsules() => [.. PlanarizedShapes.Capsules];

        /// <summary>
        /// Reconstructs the collision boxes authored with <c>planarize</c> from the plane groups no planarized capsule
        /// explains, keeping only a box that reproduces every plane it owns.
        /// </summary>
        internal List<CollisionBox> BuildPlanarizeBoxes() => [.. PlanarizedShapes.Boxes];

        private (List<CollisionCapsule> Capsules, List<CollisionBox> Boxes) PlanarizedShapes
            => planarizedShapes ??= FitPlanarizeGroups();

        private (List<CollisionCapsule> Capsules, List<CollisionBox> Boxes)? planarizedShapes;

        /// <summary>Fits every <see cref="PlanarizeGroups"/> entry once, as capsules or else as a box.</summary>
        private (List<CollisionCapsule> Capsules, List<CollisionBox> Boxes) FitPlanarizeGroups()
        {
            var capsules = new List<CollisionCapsule>();
            var boxes = new List<CollisionBox>();
            foreach (var (parent, samples, firstPlane) in PlanarizeGroups())
            {
                var priority = ColliderPriority(RigidColliderKind.CollisionPlane, firstPlane);
                var bone = ResolveRigidBone(parent);
                if (PlanarizedShapeFitter.FitPlanarizedShapes(samples) is not { } shapes)
                {
                    if (PlanarizedShapeFitter.FitOrientedPlanarizedBox(samples) is { } box
                        && SmallestVertexMapCovering(samples, [.. Enumerable.Range(0, samples.Count)]) is { } boxMap)
                    {
                        boxes.Add(new CollisionBox
                        {
                            ParentBone = bone,
                            Origin = Vector3.Transform((box.Min + box.Max) * 0.5f, box.Rotation),
                            Rotation = box.Rotation,
                            Size = (box.Max - box.Min) * 0.5f,
                            CollisionMask = PlanarizeCollisionMask,
                            VertexMap = boxMap,
                            Planarize = true,
                            PlanarizePlanes = samples.Count,
                            PlanarizeOwnPlanes = samples.Count,
                            Priority = priority,
                        });
                    }

                    continue;
                }

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

                capsules.AddRange(recovered);
            }

            return (capsules, boxes);
        }

        /// <summary>
        /// Gets the <c>m_CollisionPlanes</c> of each control parent as fit samples, with the index of the parent's first plane.
        /// </summary>
        private IEnumerable<(int Parent, List<PlanarizeSample> Samples, int FirstPlane)> PlanarizeGroups()
        {
            if (Fe.InitPosePositions.Length == 0)
            {
                yield break;
            }

            foreach (var group in Fe.CollisionPlanes.Select(static (plane, index) => (plane, index)).GroupBy(static e => e.plane.CtrlParent))
            {
                if (group.Key >= 0 && group.Key < Fe.InitPosePositions.Length)
                {
                    yield return (group.Key, PlanarizeSamples(group.Key, group.Select(static e => e.index)), group.Min(static e => e.index));
                }
            }
        }

        private List<PlanarizeSample> PlanarizeSamples(int parent, IEnumerable<int> planeIndices)
        {
            var toLocal = Quaternion.Conjugate(Fe.InitPoseRotations[parent]);
            var origin = Fe.InitPosePositions[parent];

            var samples = new List<PlanarizeSample>();
            foreach (var index in planeIndices)
            {
                var plane = Fe.CollisionPlanes[index];
                var node = plane.ChildNode;
                if (node < 0 || node >= Fe.InitPosePositions.Length)
                {
                    continue;
                }

                var x = Vector3.Transform(Fe.InitPosePositions[node] - origin, toLocal);
                var normal = plane.PlaneNormal;
                samples.Add(new PlanarizeSample(node, x, normal, plane.PlaneOffset,
                    Vector3.Dot(normal, x) - plane.PlaneOffset, Fe.GetCollisionRadius(node)));
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

                var group = GetOrAdd(groups, map);
                group.Add(member);
            }

            return [.. groups.Select(static kv => (kv.Key, kv.Value))];
        }

        /// <summary>Reconstructs the cloth collision boxes (<c>m_BoxRigids</c>).</summary>
        public List<CollisionBox> BuildCollisionBoxes()
            => ReadRigids("m_BoxRigids", RigidColliderKind.Box, static rigid =>
            {
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
                    return null;
                }

                return rigid.GetSubCollection("vSize") is { } size
                    ? new CollisionBox { Origin = origin, Rotation = rotation, Size = size.ToVector3() }
                    : null;
            });

        /// <summary>Reconstructs the cloth collision spheres (<c>m_SphereRigids</c>).</summary>
        public List<CollisionSphere> BuildCollisionSpheres()
            => ReadRigids("m_SphereRigids", RigidColliderKind.Sphere, static rigid =>
            {
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
                    return null;
                }

                return new CollisionSphere { Center = new Vector3(sphere.X, sphere.Y, sphere.Z), Radius = sphere.W };
            });
    }
}

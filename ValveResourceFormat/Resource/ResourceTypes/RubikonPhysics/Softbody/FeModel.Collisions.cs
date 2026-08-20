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
            /// Gets whether the capsule collides as a per-node plane rather than as a volume. Such a capsule
            /// leaves no rigid of its own behind, only <c>m_CollisionPlanes</c>.
            /// </summary>
            public bool Planarize { get; init; }
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

        /// <summary>
        /// Reconstructs the cloth collision capsules (<c>m_TaperedCapsuleRigids</c>). Each rigid has two
        /// spheres (the tapered end-caps); <c>vSphere[i]</c> is xyz = centre, w = radius. Returns an empty
        /// list when the model has no capsule rigids (e.g. dark_willow).
        /// </summary>
        public List<CollisionCapsule> BuildCollisionCapsules()
        {
            var result = new List<CollisionCapsule>();
            var rigids = Data.GetArray("m_TaperedCapsuleRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                var spheres = rigid.GetArray("vSphere");
                if (spheres is null || spheres.Count < 2)
                {
                    continue;
                }

                var s0 = spheres[0].ToVector4();
                var s1 = spheres[1].ToVector4();
                var node = rigid.GetInt32Property("nNode");

                var vertexMap = rigid.GetInt32Property("nVertexMapIndex");

                result.Add(new CollisionCapsule
                {
                    ParentBone = ResolveRigidBone(node),
                    Point0 = new Vector3(s0.X, s0.Y, s0.Z),
                    Radius0 = s0.W,
                    Point1 = new Vector3(s1.X, s1.Y, s1.Z),
                    Radius1 = s1.W,
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                    VertexMap = vertexMap >= 0 && vertexMap < VertexMaps.Count
                        ? VertexMaps[vertexMap].Name
                        : null,
                });
            }

            return result;
        }

        /// <summary>
        /// The plane a planarized capsule imposes on one node: the capsule surface at that node, in the
        /// parent bone's local space.
        /// </summary>
        static (Vector3 Normal, float Offset) PlanarizedSurfaceAt(Vector3 x, Vector3 c0, float r0, Vector3 c1,
            float r1)
        {
            var axis = c1 - c0;
            var length = axis.Length();
            if (length < 1e-6f)
            {
                var only = Vector3.Normalize(x - c0);
                return (only, Vector3.Dot(only, c0) + r0);
            }

            var unit = axis / length;
            var dr = r1 - r0;
            var d = x - c0;
            var along = Vector3.Dot(d, unit);
            var perp = d - (along * unit);
            var rad = perp.Length();
            var s = Math.Clamp((along * length) + (rad * dr), 0f, length * length) / (length * length);

            // A node sitting on the axis has no radial direction to build a cone normal from.
            if (rad < 1e-6f)
            {
                s = along <= 0f ? 0f : 1f;
            }

            if (s <= 0f)
            {
                var capA = Vector3.Normalize(x - c0);
                return (capA, Vector3.Dot(capA, c0) + r0);
            }

            if (s >= 1f)
            {
                var capB = Vector3.Normalize(x - c1);
                return (capB, Vector3.Dot(capB, c1) + r1);
            }

            var normal = Vector3.Normalize((length * (perp / rad)) - (dr * unit));
            var centre = c0 + (s * length * unit);
            return (normal, Vector3.Dot(normal, centre) + r0 + (s * dr));
        }

        /// <summary>
        /// Fits the sphere the tangent points of <paramref name="samples"/> lie on, given each sample's
        /// outward normal: every cap sample satisfies <c>tangent = centre + radius * normal</c> exactly, so
        /// the fit is linear and closed-form.
        /// </summary>
        static bool FitCapSphere(List<(Vector3 Tangent, Vector3 Normal)> samples, out Vector3 centre,
            out float radius)
        {
            centre = default;
            radius = 0f;
            var n = samples.Count;
            if (n < 3)
            {
                return false;
            }

            var sumN = Vector3.Zero;
            var sumB = Vector3.Zero;
            var sumBN = 0f;
            foreach (var (tangent, normal) in samples)
            {
                sumN += normal;
                sumB += tangent;
                sumBN += Vector3.Dot(tangent, normal);
            }

            var denom = ((float)n * n) - sumN.LengthSquared();
            if (Math.Abs(denom) < 1e-6f)
            {
                return false;
            }

            radius = ((sumBN * n) - Vector3.Dot(sumB, sumN)) / denom;
            centre = (sumB - (radius * sumN)) / n;
            return radius > 0f;
        }

        /// <summary>
        /// Reconstructs the collision capsules authored with <c>planarize</c> on. The compiler replaces
        /// such a capsule with one <c>m_CollisionPlanes</c> entry per node of its vertex map and writes NO
        /// <c>m_TaperedCapsuleRigids</c> record for it, so the plane set is the only trace it leaves. Each
        /// plane is the capsule surface at its node, which makes the two end-cap spheres recoverable.
        /// <para>
        /// A group is returned only when the recovered capsule reproduces every plane it owns, so a plane
        /// set this model does not explain is dropped rather than turned into a wrong collider.
        /// </para>
        /// </summary>
        public List<CollisionCapsule> BuildPlanarizeCapsules()
        {
            var result = new List<CollisionCapsule>();
            if (CollisionPlanes.Length == 0 || InitPosePositions.Length == 0)
            {
                return result;
            }

            foreach (var group in CollisionPlanes.GroupBy(static p => p.CtrlParent))
            {
                var parent = group.Key;
                if (parent < 0 || parent >= InitPosePositions.Length)
                {
                    continue;
                }

                var toLocal = Quaternion.Conjugate(InitPoseRotations[parent]);
                var origin = InitPosePositions[parent];

                var nodes = new List<int>();
                var local = new List<Vector3>();
                var planes = new List<(Vector3 Normal, float Offset)>();
                var usable = new List<(Vector3 Tangent, Vector3 Normal)>();
                foreach (var plane in group)
                {
                    var node = plane.ChildNode;
                    if (node < 0 || node >= InitPosePositions.Length)
                    {
                        continue;
                    }

                    var x = Vector3.Transform(InitPosePositions[node] - origin, toLocal);
                    var normal = plane.PlaneNormal;
                    nodes.Add(node);
                    local.Add(x);
                    planes.Add((normal, plane.PlaneOffset));

                    // A plane the compiler snapped onto its own node carries no capsule geometry any more.
                    var gap = Vector3.Dot(normal, x) - plane.PlaneOffset;
                    if (gap > 1e-4f)
                    {
                        usable.Add((x - (gap * normal), normal));
                    }
                }

                if (usable.Count < 6)
                {
                    continue;
                }

                if (!SplitCapSamples(usable, out var c0, out var r0, out var c1, out var r1))
                {
                    continue;
                }

                var reproduces = true;
                for (var i = 0; i < nodes.Count && reproduces; i++)
                {
                    var (normal, offset) = PlanarizedSurfaceAt(local[i], c0, r0, c1, r1);
                    if (Vector3.Dot(normal, local[i]) - offset < GetCollisionRadius(nodes[i]))
                    {
                        offset = Vector3.Dot(normal, local[i]);
                    }

                    reproduces = (normal - planes[i].Normal).Length() < 1e-3f
                        && Math.Abs(offset - planes[i].Offset) < 1e-2f;
                }

                if (!reproduces)
                {
                    continue;
                }

                var owned = nodes.ToHashSet();
                string? vertexMap = null;
                var mapSize = int.MaxValue;
                foreach (var map in VertexMaps)
                {
                    var covered = owned.All(node => map.WeightOf(node) > 0f);
                    if (!covered)
                    {
                        continue;
                    }

                    var size = map.Weights.Count(static w => w > 0f);
                    if (size < mapSize)
                    {
                        mapSize = size;
                        vertexMap = map.Name;
                    }
                }

                if (vertexMap is null)
                {
                    continue;
                }

                result.Add(new CollisionCapsule
                {
                    ParentBone = ResolveRigidBone(parent),
                    Point0 = c0,
                    Radius0 = r0,
                    Point1 = c1,
                    Radius1 = r1,
                    CollisionMask = 0xF,
                    VertexMap = vertexMap,
                    Planarize = true,
                });
            }

            return result;
        }

        /// <summary>
        /// Separates the tangent-point samples into the capsule's two end caps and fits each. Samples on
        /// the cone band between the caps fit neither and drop out as outliers.
        /// </summary>
        static bool SplitCapSamples(List<(Vector3 Tangent, Vector3 Normal)> samples, out Vector3 c0,
            out float r0, out Vector3 c1, out float r1)
        {
            c0 = c1 = default;
            r0 = r1 = 0f;

            var first = FindCapConsensus(samples, []);
            if (first is null)
            {
                return false;
            }

            var second = FindCapConsensus(samples, first);
            if (second is null)
            {
                return false;
            }

            return FitCapSphere(Pick(samples, first), out c0, out r0)
                && FitCapSphere(Pick(samples, second), out c1, out r1);
        }

        static List<(Vector3 Tangent, Vector3 Normal)> Pick(List<(Vector3 Tangent, Vector3 Normal)> samples,
            HashSet<int> which)
            => [.. which.Order().Select(i => samples[i])];

        // Four unknowns need only a handful of seed pairs, so the pairwise search is capped while every
        // remaining sample is still scored against each candidate.
        const int CapConsensusSeeds = 48;

        static HashSet<int>? FindCapConsensus(List<(Vector3 Tangent, Vector3 Normal)> samples,
            HashSet<int> exclude)
        {
            var pool = new List<int>(samples.Count);
            for (var s = 0; s < samples.Count; s++)
            {
                if (!exclude.Contains(s))
                {
                    pool.Add(s);
                }
            }

            if (pool.Count < 3)
            {
                return null;
            }

            HashSet<int>? best = null;
            var seeds = Math.Min(pool.Count, CapConsensusSeeds);
            for (var i = 0; i < seeds; i++)
            {
                var a = samples[pool[i]];
                for (var j = i + 1; j < pool.Count; j++)
                {
                    var b = samples[pool[j]];
                    var span = (a.Normal - b.Normal).Length();
                    if (span < 1e-3f)
                    {
                        continue;
                    }

                    var radius = (a.Tangent - b.Tangent).Length() / span;
                    var centre = a.Tangent - (radius * a.Normal);
                    var inliers = new HashSet<int>();
                    foreach (var k in pool)
                    {
                        var s = samples[k];
                        if ((s.Tangent - (centre + (radius * s.Normal))).Length() < 1e-2f)
                        {
                            inliers.Add(k);
                        }
                    }

                    if (inliers.Count > (best?.Count ?? 2))
                    {
                        best = inliers;
                    }
                }
            }

            return best;
        }

        /// <summary>
        /// Reconstructs the cloth collision boxes (<c>m_BoxRigids</c>). Returns an empty list when the
        /// model has no box rigids.
        /// </summary>
        public List<CollisionBox> BuildCollisionBoxes()
        {
            var result = new List<CollisionBox>();
            var rigids = Data.GetArray("m_BoxRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                var frame = rigid.GetSubCollection("tmFrame2");
                if (frame is null)
                {
                    continue;
                }

                var (origin, _, rotation) = frame.ToTransform();
                var node = rigid.GetInt32Property("nNode");

                result.Add(new CollisionBox
                {
                    ParentBone = ResolveRigidBone(node),
                    Origin = origin,
                    Rotation = rotation,
                    Size = rigid.GetSubCollection("vSize").ToVector3(),
                    CollisionMask = rigid.GetInt32Property("nCollisionMask"),
                });
            }

            return result;
        }

        /// <summary>
        /// Reconstructs the cloth collision spheres (<c>m_SphereRigids</c>). Returns an empty list when the
        /// model has no sphere rigids.
        /// </summary>
        public List<CollisionSphere> BuildCollisionSpheres()
        {
            var result = new List<CollisionSphere>();
            var rigids = Data.GetArray("m_SphereRigids");
            if (rigids is null)
            {
                return result;
            }

            foreach (var rigid in rigids)
            {
                // m_SphereRigids entries store a single sphere either as a flat vSphere [x,y,z,r] array
                // (unlike m_TaperedCapsuleRigids' vSphere, which nests TWO such arrays for its end-caps)
                // or m_vCenter+m_flRadius.
                Vector4 sphere;
                if (rigid.GetArray<float>("vSphere") is { Length: 4 } s)
                {
                    sphere = new Vector4(s[0], s[1], s[2], s[3]);
                }
                else if (rigid.ContainsKey("m_vSphere"))
                {
                    sphere = rigid.GetSubCollection("m_vSphere").ToVector4();
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
                });
            }

            return result;
        }
    }
}

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

        // Bit 0 of a rigid's nFlags is its inverted_collision switch.
        const uint RigidFlagInverted = 1;

        /// <summary>
        /// Reconstructs the cloth collision capsules (<c>m_TaperedCapsuleRigids</c>). Each rigid has two
        /// spheres (the tapered end-caps); <c>vSphere[i]</c> is xyz = centre, w = radius. Returns an empty
        /// list when the model has no capsule rigids.
        /// <para>
        /// Older compiles hold the end-caps as a <c>vCenter</c> pair plus an <c>flRadius</c> pair instead
        /// of <c>vSphere</c>; the oldest of them carry no vertex-map index at all.
        /// </para>
        /// </summary>
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

            var indexed = CollisionPlanes.Select(static (plane, index) => (plane, index));
            foreach (var group in indexed.GroupBy(static e => e.plane.CtrlParent))
            {
                var parent = group.Key;
                if (parent < 0 || parent >= InitPosePositions.Length)
                {
                    continue;
                }

                var toLocal = Quaternion.Conjugate(InitPoseRotations[parent]);
                var origin = InitPosePositions[parent];

                var samples = new List<PlanarizeSample>();
                foreach (var (plane, _) in group)
                {
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

                if (FitPlanarizedShapes(samples) is not { } shapes)
                {
                    continue;
                }

                var priority = ColliderPriority(RigidColliderKind.CollisionPlane,
                    group.Min(static e => e.index));
                var bone = ResolveRigidBone(parent);
                var recovered = new List<CollisionCapsule>(shapes.Count);

                foreach (var (fit, members) in shapes)
                {
                    var (point0, point1) = PlanarizedAxis(fit, samples, members);

                    if (SmallestVertexMapCovering(samples, members) is { } vertexMap)
                    {
                        recovered.Add(new CollisionCapsule
                        {
                            ParentBone = bone,
                            Point0 = point0,
                            Radius0 = fit.R0,
                            Point1 = point1,
                            Radius1 = fit.R1,
                            CollisionMask = 0xF,
                            VertexMap = vertexMap,
                            Planarize = true,
                            Priority = priority,
                        });
                        continue;
                    }

                    // No single selection covers every node the shape owns: its planes span two proxy
                    // islands. Its members are split by the smallest selection covering each one on its
                    // own, and the same fitted geometry is emitted once per selection.
                    if (SplitByVertexMap(samples, members) is not { } split)
                    {
                        recovered.Clear();
                        break;
                    }

                    foreach (var (splitMap, _) in split)
                    {
                        recovered.Add(new CollisionCapsule
                        {
                            ParentBone = bone,
                            Point0 = point0,
                            Radius0 = fit.R0,
                            Point1 = point1,
                            Radius1 = fit.R1,
                            CollisionMask = 0xF,
                            VertexMap = splitMap,
                            Planarize = true,
                            Priority = priority,
                        });
                    }
                }

                result.AddRange(recovered);
            }

            return result;
        }

        /// <summary>
        /// The two end-cap positions a recovered shape is emitted with. A fit whose caps coincide is a
        /// single end cap, and it is given a short axis pointing away from the nodes it owns, which keeps
        /// the recovered centre the nearest point on that axis and so leaves every plane unchanged.
        /// </summary>
        static (Vector3 Point0, Vector3 Point1) PlanarizedAxis(CapsuleFit fit,
            List<PlanarizeSample> samples, List<int> members)
        {
            if ((fit.C1 - fit.C0).Length() > PlanarizeCapAxisMinimum)
            {
                return (fit.C0, fit.C1);
            }

            var away = Vector3.Zero;
            foreach (var member in members)
            {
                away -= samples[member].Normal;
            }

            var length = away.Length();
            if (length < PlanarizeCapAxisMinimum)
            {
                return (fit.C0, fit.C1);
            }

            return (fit.C0, fit.C0 + (away * (PlanarizeCapAxisLength / length)));
        }

        /// <summary>
        /// One compiled collision plane, prepared for the planarized-shape fit. <c>Gap</c> is how far the
        /// node stands in front of its own plane: a plane the compiler wrote through its node rather than
        /// through the shape surface has a gap of zero and carries no geometry.
        /// </summary>
        readonly record struct PlanarizeSample(int Node, Vector3 Local, Vector3 Normal, float Offset,
            float Gap, float Radius);

        /// <summary>A recovered planarized shape. Its two end caps coincide when the shape is a sphere.</summary>
        readonly record struct CapsuleFit(Vector3 C0, float R0, Vector3 C1, float R1);

        const float PlanarizeGeometryGap = 1e-4f;
        const float PlanarizeNormalTolerance = 1e-3f;
        const float PlanarizeOffsetTolerance = 1e-2f;
        const float PlanarizeAxisTolerance = 2e-3f;
        const int PlanarizeSphereRounds = 4;
        const int PlanarizeAxisPicks = 16;
        const int PlanarizeCandidateLimit = 4096;
        const float PlanarizeCandidateGrid = 1e4f;

        // The smallest plane count a recovered shape is accepted on, and the most shapes one control
        // parent's planes may be covered by.
        const int PlanarizeMinShapePlanes = 3;
        const int PlanarizeMaxShapes = 4;

        /// <summary>
        /// Splits a control parent's collision planes into the planarized shapes that produced them, one
        /// entry per shape with the planes it owns. Returns null unless the shapes account for every plane
        /// in the group, so a plane set this model cannot explain is dropped rather than turned into a
        /// wrong collider.
        /// </summary>
        static List<(CapsuleFit Fit, List<int> Members)>? FitPlanarizedShapes(List<PlanarizeSample> samples)
        {
            if (samples.Count < 3)
            {
                return null;
            }

            var primary = PlanarizeCandidates(samples);
            if (CoverGroup(samples, primary) is { } covered)
            {
                return covered;
            }

            var widened = new List<CapsuleFit>(primary);
            widened.AddRange(NormalCandidates(samples));
            return widened.Count > primary.Count ? CoverGroup(samples, widened) : null;
        }

        // A greedy set cover of the group's planes by the candidates that reproduce them, one entry per
        // shape. Null unless the shapes account for every plane in the group.
        static List<(CapsuleFit Fit, List<int> Members)>? CoverGroup(List<PlanarizeSample> samples,
            List<CapsuleFit> fits)
        {
            var scored = new List<(CapsuleFit Fit, List<int> Members)>();
            foreach (var fit in fits)
            {
                var members = ReproducedPlanes(samples, fit);
                if (members.Count > 0)
                {
                    scored.Add((fit, members));
                }
            }

            var chosen = new List<(CapsuleFit Fit, List<int> Members)>();
            var covered = new bool[samples.Count];
            var left = samples.Count;

            while (left > 0)
            {
                var bestGain = 0;
                var bestIndex = -1;
                for (var i = 0; i < scored.Count; i++)
                {
                    var gain = scored[i].Members.Count(m => !covered[m]);
                    if (gain > bestGain)
                    {
                        bestGain = gain;
                        bestIndex = i;
                    }
                }

                if (bestIndex < 0 || bestGain < PlanarizeMinShapePlanes
                    || chosen.Count == PlanarizeMaxShapes)
                {
                    return null;
                }

                var take = scored[bestIndex].Members.Where(m => !covered[m]).ToList();
                foreach (var member in take)
                {
                    covered[member] = true;
                }

                left -= take.Count;
                chosen.Add((scored[bestIndex].Fit, take));
                scored.RemoveAt(bestIndex);
            }

            return chosen;
        }

        // Which planes of the group a candidate shape reproduces exactly, both normal and offset.
        static List<int> ReproducedPlanes(List<PlanarizeSample> samples, CapsuleFit fit)
        {
            var members = new List<int>();
            for (var i = 0; i < samples.Count; i++)
            {
                var sample = samples[i];
                var (normal, offset) = PlanarizedSurfaceAt(sample.Local, fit.C0, fit.R0, fit.C1, fit.R1);
                if (!IsFinite(normal) || !float.IsFinite(offset))
                {
                    continue;
                }

                if (Vector3.Dot(normal, sample.Local) - offset < sample.Radius)
                {
                    offset = Vector3.Dot(normal, sample.Local);
                }

                if ((normal - sample.Normal).Length() < PlanarizeNormalTolerance
                    && Math.Abs(offset - sample.Offset) < PlanarizeOffsetTolerance)
                {
                    members.Add(i);
                }
            }

            return members;
        }

        // The smallest selection covering every node one recovered shape owns.
        string? SmallestVertexMapCovering(List<PlanarizeSample> samples, List<int> members)
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
        /// Splits a shape's owned planes into selection-covered groups when no single selection covers
        /// every node the shape owns. Each plane's node is assigned the smallest selection that covers it
        /// on its own; null when any node has no covering selection at all, so a group this cannot fully
        /// explain is still dropped rather than silently losing a plane.
        /// </summary>
        List<(string Map, List<int> Members)>? SplitByVertexMap(List<PlanarizeSample> samples, List<int> members)
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

        /// <summary>
        /// Every shape the group's planes could come from: each end-cap sphere on its own, each pair of
        /// them as a capsule, each sphere extended along the axis its remaining planes imply, and, when no
        /// cap is witnessed at all, a capsule built from the cone band alone.
        /// </summary>
        static List<CapsuleFit> PlanarizeCandidates(List<PlanarizeSample> samples)
        {
            var far = new List<int>();
            for (var i = 0; i < samples.Count; i++)
            {
                if (samples[i].Gap > PlanarizeGeometryGap)
                {
                    far.Add(i);
                }
            }

            var candidates = new CandidateSet();
            var spheres = CapSphereCandidates(samples, far);

            foreach (var (centre, radius) in spheres)
            {
                candidates.Add(new CapsuleFit(centre, radius, centre, radius));
            }

            foreach (var (first, second) in spheres.SelectMany(a => spheres.Select(b => (a, b))))
            {
                if (first != second)
                {
                    candidates.Add(new CapsuleFit(first.Centre, first.Radius, second.Centre,
                        second.Radius));
                }
            }

            foreach (var (centre, radius) in spheres)
            {
                var explained = ReproducedPlanes(samples, new CapsuleFit(centre, radius, centre, radius))
                    .ToHashSet();
                var band = far.Where(i => !explained.Contains(i)).ToList();
                var axisFits = ConeAxisCandidates(samples, band);

                foreach (var (axis, cosine, _) in axisFits)
                {
                    AddAnchoredCandidates(candidates, samples, centre, radius, axis, TaperFromCosine(cosine));
                }

                // ConeAxisCandidates needs 3 band planes to seed its triple-based linear fit; one or two
                // leftover planes still over-determine the axis/length/radius once the cap sphere is
                // already known, just not linearly - fall back to the dense nonlinear solve.
                if (axisFits.Count == 0 && band.Count is > 0 and < 3
                    && NonlinearAnchoredFit(samples, band, centre, radius) is { } nonlinearFit)
                {
                    candidates.Add(nonlinearFit);
                }
            }

            var all = new List<int>(samples.Count);
            for (var i = 0; i < samples.Count; i++)
            {
                all.Add(i);
            }

            foreach (var (axis, cosine, inliers) in ConeAxisCandidates(samples, all))
            {
                AddBandCandidates(candidates, samples, far, axis, TaperFromCosine(cosine), inliers);
            }

            return candidates.Fits;
        }

        // A cone-band normal makes a constant angle with the axis: cos = -taper / sqrt(1 + taper^2).
        static float TaperFromCosine(float cosine)
            => -cosine / MathF.Sqrt(1f - (cosine * cosine));

        // How far outside the planes it owns a recovered cap is placed, and how many cap positions read
        // off a leftover plane are tried alongside those.
        static readonly float[] PlanarizeCapMargins = [0.25f, 1f, 4f];
        static readonly float[] PlanarizeRadiusMargins = [0.05f, 0.5f, 2f];

        // A recovered shape whose end caps coincide compiles as a sphere, and a planarized sphere loses
        // every node a planarized capsule also covers, so such a cap is given a short axis instead.
        const float PlanarizeCapAxisMinimum = 1e-4f;
        const float PlanarizeCapAxisLength = 0.01f;
        const int PlanarizeCapPicks = 4;
        const int PlanarizeSubsetRounds = 3;

        /// <summary>
        /// Every shape the group's planes could come from when read through their NORMALS rather than
        /// their offsets. A plane the compiler clamped to its own node keeps an exact normal, and a
        /// normal alone fixes the axis line, the taper and an end-cap centre, so a group whose planes are
        /// mostly or entirely clamped is still reconstructible: what the clamp leaves free is a radius
        /// and where the caps sit, and every choice of those reproduces the same compiled planes.
        /// <para>
        /// The search runs on the whole group, then on the planes the first axis did not claim, so a
        /// control parent carrying two shapes separates instead of being fitted as one.
        /// </para>
        /// </summary>
        static List<CapsuleFit> NormalCandidates(List<PlanarizeSample> samples)
        {
            var candidates = new CandidateSet();
            var remaining = new List<int>(samples.Count);
            for (var i = 0; i < samples.Count; i++)
            {
                remaining.Add(i);
            }

            for (var round = 0; round < PlanarizeSubsetRounds; round++)
            {
                if (remaining.Count < PlanarizeMinShapePlanes)
                {
                    break;
                }

                AddCapSpheres(candidates, samples, remaining);

                var axes = NormalAxisCandidates(samples, remaining);
                if (axes.Count == 0)
                {
                    break;
                }

                foreach (var (axis, cosine, inliers) in axes)
                {
                    AddBandCapsules(candidates, samples, inliers, axis, TaperFromCosine(cosine));
                    AddCapSpheres(candidates, samples, inliers);
                }

                var claimed = axes[0].Inliers.ToHashSet();
                if (claimed.Count == remaining.Count)
                {
                    break;
                }

                remaining = [.. remaining.Where(i => !claimed.Contains(i))];
            }

            return candidates.Fits;
        }

        /// <summary>
        /// <see cref="ConeAxisCandidates"/> over the plane normals with an exact axis refit, and a
        /// consensus that is only ever allowed to grow.
        /// </summary>
        static List<(Vector3 Axis, float Cosine, List<int> Inliers)> NormalAxisCandidates(
            List<PlanarizeSample> samples, List<int> subset)
        {
            var results = new List<(Vector3 Axis, float Cosine, List<int> Inliers)>();
            if (subset.Count < 3)
            {
                return results;
            }

            List<int>? best = null;
            foreach (var (a, b, c) in AxisSeedTriples(subset))
            {
                var span = Vector3.Cross(samples[b].Normal - samples[a].Normal,
                    samples[c].Normal - samples[a].Normal);
                if (span.Length() < 1e-4f)
                {
                    continue;
                }

                var axis = Vector3.Normalize(span);
                var cosine = Vector3.Dot(samples[a].Normal, axis);
                if (MathF.Abs(cosine) >= 0.999f)
                {
                    continue;
                }

                var inliers = AxisInliers(samples, subset, axis, cosine);
                if (inliers.Count >= 3 && inliers.Count > (best?.Count ?? 2))
                {
                    best = inliers;
                }
            }

            if (best is null)
            {
                return results;
            }

            for (var pass = 0; pass < 4; pass++)
            {
                if (!RefitAxisExact(samples, best, out var axis, out var cosine))
                {
                    break;
                }

                var inliers = AxisInliers(samples, subset, axis, cosine);
                if (inliers.Count <= best.Count)
                {
                    break;
                }

                best = inliers;
            }

            if (!RefitAxisExact(samples, best, out var finalAxis, out var finalCosine)
                || MathF.Abs(finalCosine) >= 0.999f)
            {
                return results;
            }

            results.Add((finalAxis, finalCosine, best));
            results.Add((-finalAxis, -finalCosine, best));
            return results;
        }

        // The axis is the least-variance direction of the inlier normals, taken from the closed-form
        // smallest eigenvector of their covariance.
        static bool RefitAxisExact(List<PlanarizeSample> samples, List<int> inliers, out Vector3 axis,
            out float cosine)
        {
            axis = Vector3.UnitX;
            cosine = 0f;
            if (inliers.Count < 3)
            {
                return false;
            }

            var mean = Vector3.Zero;
            foreach (var i in inliers)
            {
                mean += samples[i].Normal;
            }

            mean /= inliers.Count;

            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var i in inliers)
            {
                var d = samples[i].Normal - mean;
                xx += (double)d.X * d.X;
                xy += (double)d.X * d.Y;
                xz += (double)d.X * d.Z;
                yy += (double)d.Y * d.Y;
                yz += (double)d.Y * d.Z;
                zz += (double)d.Z * d.Z;
            }

            if (!SmallestEigenvector(xx, xy, xz, yy, yz, zz, out axis))
            {
                return false;
            }

            cosine = Vector3.Dot(mean, axis);
            return true;
        }

        /// <summary>
        /// The unit eigenvector of the smallest eigenvalue of a symmetric 3x3 matrix, in closed form:
        /// the eigenvalue from the trigonometric solution of its characteristic cubic, the vector from
        /// the longest cross product of two rows of the shifted matrix.
        /// </summary>
        static bool SmallestEigenvector(double xx, double xy, double xz, double yy, double yz, double zz,
            out Vector3 vector)
        {
            vector = Vector3.UnitX;
            var q = (xx + yy + zz) / 3;
            var offDiagonal = (xy * xy) + (xz * xz) + (yz * yz);
            if (offDiagonal <= 1e-30)
            {
                vector = xx <= yy && xx <= zz ? Vector3.UnitX : yy <= zz ? Vector3.UnitY : Vector3.UnitZ;
                return true;
            }

            var spread = ((xx - q) * (xx - q)) + ((yy - q) * (yy - q)) + ((zz - q) * (zz - q))
                + (2 * offDiagonal);
            var p = Math.Sqrt(spread / 6);
            if (p < 1e-30)
            {
                return false;
            }

            var a = (xx - q) / p;
            var b = xy / p;
            var c = xz / p;
            var d = (yy - q) / p;
            var e = yz / p;
            var f = (zz - q) / p;
            var determinant = (a * ((d * f) - (e * e))) - (b * ((b * f) - (e * c)))
                + (c * ((b * e) - (d * c)));
            var phi = Math.Acos(Math.Clamp(determinant / 2, -1.0, 1.0)) / 3;
            var smallest = q + (2 * p * Math.Cos(phi + (2 * Math.PI / 3)));

            Span<Vector3> rows =
            [
                new((float)(xx - smallest), (float)xy, (float)xz),
                new((float)xy, (float)(yy - smallest), (float)yz),
                new((float)xz, (float)yz, (float)(zz - smallest)),
            ];

            var bestLength = 0f;
            var found = Vector3.Zero;
            for (var i = 0; i < 3; i++)
            {
                for (var j = i + 1; j < 3; j++)
                {
                    var cross = Vector3.Cross(rows[i], rows[j]);
                    var length = cross.Length();
                    if (length > bestLength)
                    {
                        bestLength = length;
                        found = cross;
                    }
                }
            }

            if (bestLength < 1e-20f || !IsFinite(found))
            {
                return false;
            }

            vector = found / bestLength;
            return true;
        }

        // The unit direction from the axis out to a node, read back off its plane normal.
        static bool RadialDirection(Vector3 normal, Vector3 unit, float taper, out Vector3 radial)
        {
            radial = (normal * MathF.Sqrt(1f + (taper * taper))) + (taper * unit);
            radial -= Vector3.Dot(radial, unit) * unit;
            var length = radial.Length();
            if (length < 1e-6f)
            {
                return false;
            }

            radial /= length;
            return true;
        }

        /// <summary>
        /// Where the capsule axis crosses the plane through the origin perpendicular to it. Each node
        /// sits at <c>perp(x) = centre + radius * radial</c>, so the centre lies on the line through
        /// <c>perp(x)</c> along that radial direction, which is one linear equation per plane and needs
        /// no gap - a clamped plane counts for this fit exactly as much as an unclamped one.
        /// </summary>
        static bool AxisLineFromNormals(List<PlanarizeSample> samples, List<int> subset, Vector3 unit,
            float taper, out Vector3 centre)
        {
            centre = default;
            var first = MathF.Abs(unit.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            first -= Vector3.Dot(first, unit) * unit;
            if (first.Length() < 1e-6f)
            {
                return false;
            }

            first = Vector3.Normalize(first);
            var second = Vector3.Cross(unit, first);

            double m00 = 0, m01 = 0, m11 = 0, b0 = 0, b1 = 0;
            var rows = 0;
            foreach (var i in subset)
            {
                if (!RadialDirection(samples[i].Normal, unit, taper, out var radial))
                {
                    return false;
                }

                var edge = Vector3.Cross(unit, radial);
                double u = Vector3.Dot(first, edge);
                double v = Vector3.Dot(second, edge);
                double rhs = Vector3.Dot(samples[i].Local, edge);
                m00 += u * u;
                m01 += u * v;
                m11 += v * v;
                b0 += u * rhs;
                b1 += v * rhs;
                rows++;
            }

            var determinant = (m00 * m11) - (m01 * m01);
            if (rows < 2 || Math.Abs(determinant) < 1e-12)
            {
                return false;
            }

            var alpha = ((b0 * m11) - (b1 * m01)) / determinant;
            var beta = ((b1 * m00) - (b0 * m01)) / determinant;
            centre = ((float)alpha * first) + ((float)beta * second);
            return IsFinite(centre);
        }

        /// <summary>
        /// Capsules whose cone band carries every plane of <paramref name="subset"/>. The axis line and
        /// the taper come from the normals; the radius from the unclamped gaps, or, when the subset has
        /// none, from the smallest radius that keeps every one of its planes clamped; the caps are placed
        /// outside the planes they own, and at the position any leftover plane implies when read as a cap
        /// tangency.
        /// </summary>
        static void AddBandCapsules(CandidateSet candidates, List<PlanarizeSample> samples,
            List<int> subset, Vector3 unit, float taper)
        {
            if (!AxisLineFromNormals(samples, subset, unit, taper, out var line))
            {
                return;
            }

            var scale = MathF.Sqrt(1f + (taper * taper)) - (taper * taper);
            if (MathF.Abs(scale) < 1e-6f)
            {
                return;
            }

            var radii = new float[samples.Count];
            var lowest = float.MaxValue;
            var highest = float.MinValue;
            var gapped = 0;
            var radiusSum = 0f;
            var floor = float.MinValue;

            foreach (var i in subset)
            {
                if (!RadialDirection(samples[i].Normal, unit, taper, out var radial))
                {
                    return;
                }

                var along = Vector3.Dot(samples[i].Local, unit);
                var perpendicular = samples[i].Local - (along * unit);
                var radius = Vector3.Dot(perpendicular - line, radial);
                if (radius <= 0f)
                {
                    return;
                }

                radii[i] = radius;
                var reach = (radius * scale) - (taper * along);
                if (samples[i].Gap > PlanarizeGeometryGap)
                {
                    gapped++;
                    radiusSum += reach - samples[i].Gap;
                }
                else
                {
                    floor = MathF.Max(floor, reach - samples[i].Radius);
                }

                var position = along + (radius * taper);
                lowest = MathF.Min(lowest, position);
                highest = MathF.Max(highest, position);
            }

            var betas = new List<float>();
            if (gapped > 0)
            {
                betas.Add(radiusSum / gapped);
            }

            if (floor > float.MinValue)
            {
                foreach (var margin in PlanarizeRadiusMargins)
                {
                    betas.Add(floor + margin);
                }
            }

            var inside = subset.ToHashSet();
            var starts = new List<float>(PlanarizeCapMargins.Select(m => lowest - m));
            var ends = new List<float>(PlanarizeCapMargins.Select(m => highest + m));
            var extraStarts = new List<float>();
            var extraEnds = new List<float>();

            for (var i = 0; i < samples.Count; i++)
            {
                if (inside.Contains(i) || !CapPositionOnAxis(samples[i], line, unit, out var position))
                {
                    continue;
                }

                if (position < lowest)
                {
                    extraStarts.Add(position);
                }
                else if (position > highest)
                {
                    extraEnds.Add(position);
                }
            }

            extraStarts.Sort(static (a, b) => b.CompareTo(a));
            extraEnds.Sort();
            starts.AddRange(extraStarts.Take(PlanarizeCapPicks));
            ends.AddRange(extraEnds.Take(PlanarizeCapPicks));

            foreach (var beta in betas)
            {
                foreach (var start in starts)
                {
                    var r0 = beta + (taper * start);
                    if (r0 <= 0f)
                    {
                        continue;
                    }

                    foreach (var end in ends)
                    {
                        var r1 = beta + (taper * end);
                        if (r1 <= 0f || end <= start)
                        {
                            continue;
                        }

                        candidates.Add(new CapsuleFit(line + (start * unit), r0, line + (end * unit), r1));
                    }
                }
            }
        }

        // Where along the axis an end cap must sit for one plane to be its tangent: the cap centre lies
        // both on the axis and on the ray running back along the plane normal from the node.
        static bool CapPositionOnAxis(PlanarizeSample sample, Vector3 line, Vector3 unit,
            out float position)
        {
            position = 0f;
            var cross = Vector3.Dot(unit, sample.Normal);
            var determinant = 1f - (cross * cross);
            if (MathF.Abs(determinant) < 1e-6f)
            {
                return false;
            }

            var offset = sample.Local - line;
            var alongAxis = Vector3.Dot(offset, unit);
            var alongNormal = Vector3.Dot(offset, sample.Normal);
            var back = (alongNormal - (cross * alongAxis)) / determinant;
            if (back <= 0f)
            {
                return false;
            }

            position = alongAxis - (cross * back);
            return float.IsFinite(position);
        }

        /// <summary>
        /// The end cap every plane normal of <paramref name="subset"/> radiates from, as a planarized
        /// sphere. A cap plane's normal points straight out of the cap centre, so the centre is the point
        /// closest to all the rays those normals run back along - again with no gap needed, which is what
        /// makes an entirely clamped cap recoverable.
        /// </summary>
        static void AddCapSpheres(CandidateSet candidates, List<PlanarizeSample> samples, List<int> subset)
        {
            double m00 = 0, m01 = 0, m02 = 0, m11 = 0, m12 = 0, m22 = 0, b0 = 0, b1 = 0, b2 = 0;
            foreach (var i in subset)
            {
                var n = samples[i].Normal;
                var x = samples[i].Local;
                double xx = 1 - ((double)n.X * n.X);
                double xy = -((double)n.X * n.Y);
                double xz = -((double)n.X * n.Z);
                double yy = 1 - ((double)n.Y * n.Y);
                double yz = -((double)n.Y * n.Z);
                double zz = 1 - ((double)n.Z * n.Z);
                m00 += xx;
                m01 += xy;
                m02 += xz;
                m11 += yy;
                m12 += yz;
                m22 += zz;
                b0 += (xx * x.X) + (xy * x.Y) + (xz * x.Z);
                b1 += (xy * x.X) + (yy * x.Y) + (yz * x.Z);
                b2 += (xz * x.X) + (yz * x.Y) + (zz * x.Z);
            }

            var c00 = (m11 * m22) - (m12 * m12);
            var c01 = (m02 * m12) - (m01 * m22);
            var c02 = (m01 * m12) - (m02 * m11);
            var determinant = (m00 * c00) + (m01 * c01) + (m02 * c02);
            if (Math.Abs(determinant) < 1e-9)
            {
                return;
            }

            var c11 = (m00 * m22) - (m02 * m02);
            var c12 = (m01 * m02) - (m00 * m12);
            var c22 = (m00 * m11) - (m01 * m01);
            var centre = new Vector3(
                (float)(((c00 * b0) + (c01 * b1) + (c02 * b2)) / determinant),
                (float)(((c01 * b0) + (c11 * b1) + (c12 * b2)) / determinant),
                (float)(((c02 * b0) + (c12 * b1) + (c22 * b2)) / determinant));
            if (!IsFinite(centre))
            {
                return;
            }

            var gapped = 0;
            var radiusSum = 0f;
            var floor = float.MinValue;
            foreach (var i in subset)
            {
                var distance = (samples[i].Local - centre).Length();
                if (distance <= 1e-6f)
                {
                    return;
                }

                if (samples[i].Gap > PlanarizeGeometryGap)
                {
                    gapped++;
                    radiusSum += distance - samples[i].Gap;
                }
                else
                {
                    floor = MathF.Max(floor, distance - samples[i].Radius);
                }
            }

            if (gapped > 0)
            {
                candidates.Add(new CapsuleFit(centre, radiusSum / gapped, centre, radiusSum / gapped));
            }

            if (floor > float.MinValue)
            {
                foreach (var margin in PlanarizeRadiusMargins)
                {
                    candidates.Add(new CapsuleFit(centre, floor + margin, centre, floor + margin));
                }
            }
        }

        // The end-cap spheres the far planes' tangent points lie on, one per consensus round.
        static List<(Vector3 Centre, float Radius)> CapSphereCandidates(List<PlanarizeSample> samples,
            List<int> far)
        {
            var tangents = new List<(Vector3 Tangent, Vector3 Normal)>(far.Count);
            foreach (var i in far)
            {
                var sample = samples[i];
                tangents.Add((sample.Local - (sample.Gap * sample.Normal), sample.Normal));
            }

            var spheres = new List<(Vector3 Centre, float Radius)>();
            var used = new HashSet<int>();

            for (var round = 0; round < PlanarizeSphereRounds; round++)
            {
                var consensus = FindCapConsensus(tangents, used);
                if (consensus is null)
                {
                    break;
                }

                if (FitCapSphere(Pick(tangents, consensus), out var centre, out var radius))
                {
                    spheres.Add((centre, radius));
                }

                used.UnionWith(consensus);
                if (used.Count >= tangents.Count)
                {
                    break;
                }
            }

            return spheres;
        }

        // Damped Gauss-Newton iteration cap and convergence floor for NonlinearAnchoredFit.
        const int NonlinearMaxIterations = 60;
        const double NonlinearConvergedCost = 1e-8;

        // The cone-band formula alone, without PlanarizedSurfaceAt's end-cap branches or its clamp on s,
        // so it stays smooth and differentiable for every s.
        static bool BandSurfaceAt(Vector3 x, Vector3 c0, float r0, Vector3 c1, float r1, out Vector3 normal,
            out float offset)
        {
            normal = default;
            offset = 0f;
            var axis = c1 - c0;
            var length = axis.Length();
            if (length < 1e-6f)
            {
                return false;
            }

            var unit = axis / length;
            var dr = r1 - r0;
            var d = x - c0;
            var along = Vector3.Dot(d, unit);
            var perp = d - (along * unit);
            var rad = perp.Length();
            if (rad < 1e-6f)
            {
                return false;
            }

            var s = ((along * length) + (rad * dr)) / (length * length);
            normal = Vector3.Normalize((length * (perp / rad)) - (dr * unit));
            var centre = c0 + (s * length * unit);
            offset = Vector3.Dot(normal, centre) + r0 + (s * dr);
            return IsFinite(normal) && float.IsFinite(offset);
        }

        /// <summary>
        /// Fits the far cap's parent capsule (near centre, near radius) from one already-known end-cap
        /// sphere and too few band planes for <see cref="ConeAxisCandidates"/>'s own triple-based linear
        /// fit (one or two of them). A damped Gauss-Newton search over the near centre and radius,
        /// minimising the <see cref="BandSurfaceAt"/> residual against every unexplained plane directly.
        /// </summary>
        static CapsuleFit? NonlinearAnchoredFit(List<PlanarizeSample> samples, List<int> unexplained,
            Vector3 capCentre, float capRadius)
        {
            if (unexplained.Count == 0)
            {
                return null;
            }

            // A band sample's normal is dominated by the radial direction off the axis, so the axis seeds
            // are taken from the first unexplained sample's normal: two directions per sign, one from that
            // normal and one perpendicular to it and to local Z, over a spread of length scales. Each seed
            // is run to convergence and the lowest-cost result wins.
            var normal0 = samples[unexplained[0]].Normal;
            var perpendicular = Vector3.Cross(normal0, Vector3.UnitZ);
            if (perpendicular.LengthSquared() < 1e-6f)
            {
                perpendicular = Vector3.Cross(normal0, Vector3.UnitX);
            }

            perpendicular = perpendicular.LengthSquared() > 1e-6f ? Vector3.Normalize(perpendicular) : normal0;

            var seedDirections = new List<Vector3> { perpendicular, -perpendicular, normal0, -normal0 };
            var seedLengths = new List<float> { 2f, 5f, 10f, 20f, 40f, 80f };

            CapsuleFit? best = null;
            var bestCost = double.PositiveInfinity;

            foreach (var direction in seedDirections)
            {
                foreach (var seedLength in seedLengths)
                {
                    var seedC0 = capCentre - (seedLength * direction);
                    if (RunFrom(seedC0, capRadius) is not { } result || result.Cost >= bestCost
                        || !EveryUnexplainedIsOnTheBand(result.C0, result.R0))
                    {
                        continue;
                    }

                    bestCost = result.Cost;
                    best = new CapsuleFit(result.C0, result.R0, capCentre, capRadius);
                }
            }

            // Two samples pin the axis only up to a mirror ambiguity: a capsule on the far side of the far
            // cap, growing the opposite way, reproduces the same two planes. A candidate is kept only when
            // the unexplained samples it was fitted to land in (0,1) on its own axis.
            bool EveryUnexplainedIsOnTheBand(Vector3 c0, float r0)
            {
                var axis = capCentre - c0;
                var length = axis.Length();
                if (length < 1e-6f)
                {
                    return false;
                }

                var unit = axis / length;
                var dr = capRadius - r0;
                foreach (var index in unexplained)
                {
                    var d = samples[index].Local - c0;
                    var along = Vector3.Dot(d, unit);
                    var rad = (d - (along * unit)).Length();
                    var s = ((along * length) + (rad * dr)) / (length * length);
                    if (s is < -0.02f or > 1.02f)
                    {
                        return false;
                    }
                }

                return true;
            }

            return bestCost <= NonlinearConvergedCost ? best : null;

            (Vector3 C0, float R0, double Cost)? RunFrom(Vector3 c0, float r0)
            {
                var lambda = 1e-3;

                var (residual, cost) = Evaluate(c0, r0);
                if (!double.IsFinite(cost))
                {
                    return null;
                }

                const float Eps = 1e-3f;
                for (var iteration = 0; iteration < NonlinearMaxIterations && cost > NonlinearConvergedCost; iteration++)
                {
                    var (residualX, _) = Evaluate(c0 + new Vector3(Eps, 0, 0), r0);
                    var (residualY, _) = Evaluate(c0 + new Vector3(0, Eps, 0), r0);
                    var (residualZ, _) = Evaluate(c0 + new Vector3(0, 0, Eps), r0);
                    var (residualR0, _) = Evaluate(c0, r0 + Eps);

                    var normalMatrix = new double[4, 5];
                    for (var row = 0; row < residual.Length; row++)
                    {
                        double[] gradient =
                        [
                            (residualX[row] - residual[row]) / Eps,
                            (residualY[row] - residual[row]) / Eps,
                            (residualZ[row] - residual[row]) / Eps,
                            (residualR0[row] - residual[row]) / Eps,
                        ];

                        for (var r = 0; r < 4; r++)
                        {
                            for (var c = 0; c < 4; c++)
                            {
                                normalMatrix[r, c] += gradient[r] * gradient[c];
                            }

                            normalMatrix[r, 4] -= gradient[r] * residual[row];
                        }
                    }

                    for (var d = 0; d < 4; d++)
                    {
                        normalMatrix[d, d] = (normalMatrix[d, d] * (1 + lambda)) + 1e-9;
                    }

                    if (!SolveInPlace(normalMatrix, out var delta))
                    {
                        lambda *= 3;
                        continue;
                    }

                    var candidateC0 = c0 + new Vector3((float)delta[0], (float)delta[1], (float)delta[2]);
                    var candidateR0 = r0 + (float)delta[3];
                    if (candidateR0 <= 0f || !IsFinite(candidateC0))
                    {
                        lambda *= 3;
                        continue;
                    }

                    var (candidateResidual, candidateCost) = Evaluate(candidateC0, candidateR0);
                    if (double.IsFinite(candidateCost) && candidateCost < cost)
                    {
                        c0 = candidateC0;
                        r0 = candidateR0;
                        residual = candidateResidual;
                        cost = candidateCost;
                        lambda = Math.Max(lambda / 3, 1e-9);
                    }
                    else
                    {
                        lambda *= 3;
                    }
                }

                return (c0, r0, cost);
            }

            (double[] Residual, double Cost) Evaluate(Vector3 testC0, float testR0)
            {
                var residual = new double[unexplained.Count * 4];
                double cost = 0;
                for (var i = 0; i < unexplained.Count; i++)
                {
                    var sample = samples[unexplained[i]];
                    if (!BandSurfaceAt(sample.Local, testC0, testR0, capCentre, capRadius, out var normal,
                        out var offset))
                    {
                        return (residual, double.PositiveInfinity);
                    }

                    var baseIndex = i * 4;
                    residual[baseIndex] = normal.X - sample.Normal.X;
                    residual[baseIndex + 1] = normal.Y - sample.Normal.Y;
                    residual[baseIndex + 2] = normal.Z - sample.Normal.Z;
                    residual[baseIndex + 3] = offset - sample.Offset;
                    for (var k = 0; k < 4; k++)
                    {
                        cost += residual[baseIndex + k] * residual[baseIndex + k];
                    }
                }

                return (residual, cost);
            }
        }

        /// <summary>
        /// Extends one recovered cap sphere into capsules along <paramref name="axis"/>. The length is not
        /// determined by the cone band, so the candidates are the shortest capsule that keeps every plane
        /// off the far cap, a few multiples of it, and the length each plane implies when read as a far-cap
        /// tangency.
        /// </summary>
        static void AddAnchoredCandidates(CandidateSet candidates, List<PlanarizeSample> samples,
            Vector3 centre, float radius, Vector3 axis, float taper)
        {
            var shortest = 0f;
            foreach (var sample in samples)
            {
                var offset = sample.Local - centre;
                var along = Vector3.Dot(offset, axis);
                var perpendicular = (offset - (along * axis)).Length();
                shortest = MathF.Max(shortest, along + (perpendicular * taper));
            }

            var lengths = new List<float>();
            foreach (var multiple in (float[])[1f, 1.02f, 1.1f, 1.3f, 2f])
            {
                if (shortest > 0f)
                {
                    lengths.Add((shortest * multiple) + 1e-2f);
                }
            }

            AddLengthCandidates(lengths, samples, centre, radius, axis, taper);

            foreach (var length in lengths)
            {
                candidates.Add(new CapsuleFit(centre, radius, centre + (length * axis),
                    radius + (taper * length)));
            }
        }

        /// <summary>
        /// Builds capsules from the cone band alone, for a group where no end cap is witnessed. The band
        /// fixes the axis, the taper and the axis's perpendicular position; the two remaining degrees of
        /// freedom, where the caps sit along the axis, are taken just outside the planes they own.
        /// </summary>
        static void AddBandCandidates(CandidateSet candidates, List<PlanarizeSample> samples,
            List<int> far, Vector3 axis, float taper, List<int> inliers)
        {
            var band = inliers.Where(far.Contains).ToList();
            if (!PerpendicularCentre(samples, band, axis, taper, out var perpendicular, out var beta))
            {
                return;
            }

            var lowest = float.MaxValue;
            var highest = float.MinValue;
            foreach (var sample in samples)
            {
                var along = Vector3.Dot(sample.Local, axis);
                lowest = MathF.Min(lowest, along);
                highest = MathF.Max(highest, along);
            }

            foreach (var before in (float[])[0.25f, 1f, 3f])
            {
                var start = lowest - before;
                var c0 = perpendicular + (start * axis);
                var r0 = beta + (taper * start);
                if (r0 <= 0f)
                {
                    continue;
                }

                var lengths = new List<float>();
                foreach (var after in (float[])[0.25f, 1f, 3f])
                {
                    lengths.Add(highest + after - start);
                }

                AddLengthCandidates(lengths, samples, c0, r0, axis, taper);

                foreach (var length in lengths)
                {
                    candidates.Add(new CapsuleFit(c0, r0, c0 + (length * axis),
                        r0 + (taper * length)));
                }
            }
        }

        // The capsule length implied by reading one plane as a tangency on the far cap.
        static void AddLengthCandidates(List<float> lengths, List<PlanarizeSample> samples, Vector3 centre,
            float radius, Vector3 axis, float taper)
        {
            var origin = Vector3.Dot(centre, axis);
            foreach (var sample in samples)
            {
                var cosine = Vector3.Dot(sample.Normal, axis);
                var denominator = 1f + (taper * cosine);
                if (MathF.Abs(denominator) < 1e-6f)
                {
                    continue;
                }

                var length = (Vector3.Dot(sample.Local, axis) - origin
                    - ((sample.Gap + radius) * cosine)) / denominator;
                if (length is > 1e-3f and < 1e4f)
                {
                    lengths.Add(length);
                }
            }
        }

        /// <summary>
        /// The axis the cone-band planes of <paramref name="subset"/> share, with its normal cosine. Every
        /// plane tangent to the band satisfies <c>dot(normal, axis) = -taper / sqrt(1 + taper^2)</c>, so the
        /// band normals lie on one plane in normal space. The search seeds on normal triples and refits on
        /// the inliers, which separates the band from the end-cap planes mixed in with it. Both axis
        /// directions are returned because the sign decides which end is the first cap.
        /// </summary>
        static List<(Vector3 Axis, float Cosine, List<int> Inliers)> ConeAxisCandidates(
            List<PlanarizeSample> samples, List<int> subset)
        {
            var results = new List<(Vector3 Axis, float Cosine, List<int> Inliers)>();
            if (subset.Count < 3)
            {
                return results;
            }

            List<int>? best = null;
            foreach (var (a, b, c) in AxisSeedTriples(subset))
            {
                var span = Vector3.Cross(samples[b].Normal - samples[a].Normal,
                    samples[c].Normal - samples[a].Normal);
                if (span.Length() < 1e-4f)
                {
                    continue;
                }

                var axis = Vector3.Normalize(span);
                var cosine = Vector3.Dot(samples[a].Normal, axis);
                if (MathF.Abs(cosine) >= 0.999f)
                {
                    continue;
                }

                var inliers = AxisInliers(samples, subset, axis, cosine);
                if (inliers.Count >= 3 && inliers.Count > (best?.Count ?? 2))
                {
                    best = inliers;
                }
            }

            if (best is null)
            {
                return results;
            }

            for (var pass = 0; pass < 4; pass++)
            {
                if (!RefitAxis(samples, best, out var axis, out var cosine))
                {
                    break;
                }

                var inliers = AxisInliers(samples, subset, axis, cosine);
                if (inliers.Count < 3 || inliers.Count == best.Count)
                {
                    break;
                }

                best = inliers;
            }

            if (!RefitAxis(samples, best, out var finalAxis, out var finalCosine)
                || MathF.Abs(finalCosine) >= 0.999f)
            {
                return results;
            }

            results.Add((finalAxis, finalCosine, best));
            results.Add((-finalAxis, -finalCosine, best));
            return results;
        }

        // Deterministic seeds: every triple of up to 16 evenly spread planes, plus every adjacent triple.
        static IEnumerable<(int A, int B, int C)> AxisSeedTriples(List<int> subset)
        {
            var picks = new List<int>();
            if (subset.Count <= PlanarizeAxisPicks)
            {
                picks.AddRange(subset);
            }
            else
            {
                for (var pick = 0; pick < PlanarizeAxisPicks; pick++)
                {
                    var index = (int)Math.Round((double)pick * (subset.Count - 1)
                        / (PlanarizeAxisPicks - 1));
                    if (picks.Count == 0 || picks[^1] != subset[index])
                    {
                        picks.Add(subset[index]);
                    }
                }
            }

            for (var i = 0; i < picks.Count; i++)
            {
                for (var j = i + 1; j < picks.Count; j++)
                {
                    for (var k = j + 1; k < picks.Count; k++)
                    {
                        yield return (picks[i], picks[j], picks[k]);
                    }
                }
            }

            for (var i = 0; i + 2 < subset.Count; i++)
            {
                yield return (subset[i], subset[i + 1], subset[i + 2]);
            }
        }

        static List<int> AxisInliers(List<PlanarizeSample> samples, List<int> subset, Vector3 axis,
            float cosine)
        {
            var inliers = new List<int>();
            foreach (var i in subset)
            {
                if (MathF.Abs(Vector3.Dot(samples[i].Normal, axis) - cosine) < PlanarizeAxisTolerance)
                {
                    inliers.Add(i);
                }
            }

            return inliers;
        }

        // The axis is the least-variance direction of the inlier normals. A power iteration on the
        // covariance shifted by its own trace converges to it without a full eigen decomposition.
        static bool RefitAxis(List<PlanarizeSample> samples, List<int> inliers, out Vector3 axis,
            out float cosine)
        {
            axis = Vector3.UnitX;
            cosine = 0f;
            if (inliers.Count < 3)
            {
                return false;
            }

            var mean = Vector3.Zero;
            foreach (var i in inliers)
            {
                mean += samples[i].Normal;
            }

            mean /= inliers.Count;

            double xx = 0, xy = 0, xz = 0, yy = 0, yz = 0, zz = 0;
            foreach (var i in inliers)
            {
                var d = samples[i].Normal - mean;
                xx += (double)d.X * d.X;
                xy += (double)d.X * d.Y;
                xz += (double)d.X * d.Z;
                yy += (double)d.Y * d.Y;
                yz += (double)d.Y * d.Z;
                zz += (double)d.Z * d.Z;
            }

            var trace = xx + yy + zz;
            double vx = 0.5773502691896258, vy = 0.5773502691896258, vz = 0.5773502691896258;
            for (var iteration = 0; iteration < 64; iteration++)
            {
                var nx = ((trace - xx) * vx) - (xy * vy) - (xz * vz);
                var ny = (-xy * vx) + ((trace - yy) * vy) - (yz * vz);
                var nz = (-xz * vx) - (yz * vy) + ((trace - zz) * vz);
                var length = Math.Sqrt((nx * nx) + (ny * ny) + (nz * nz));
                if (length < 1e-20)
                {
                    return false;
                }

                vx = nx / length;
                vy = ny / length;
                vz = nz / length;
            }

            axis = new Vector3((float)vx, (float)vy, (float)vz);
            if (!IsFinite(axis))
            {
                return false;
            }

            cosine = Vector3.Dot(mean, axis);
            return true;
        }

        /// <summary>
        /// Where the capsule axis passes through the plane perpendicular to it, plus the first cap radius
        /// measured from that plane. With the axis and the taper known, a band plane's own node satisfies
        /// <c>|perp(x) - perp(c0)| * scale = gap + taper * dot(x, axis) + beta</c>, which squares into a
        /// linear system in the perpendicular centre, <c>beta</c> and one slack term.
        /// </summary>
        static bool PerpendicularCentre(List<PlanarizeSample> samples, List<int> band, Vector3 axis,
            float taper, out Vector3 centre, out float beta)
        {
            centre = default;
            beta = 0f;
            var scale = MathF.Sqrt(1f + (taper * taper)) - (taper * taper);
            if (band.Count < 4 || MathF.Abs(scale) < 1e-6f)
            {
                return false;
            }

            var first = MathF.Abs(axis.X) < 0.9f ? Vector3.UnitX : Vector3.UnitY;
            first -= Vector3.Dot(first, axis) * axis;
            if (first.Length() < 1e-6f)
            {
                return false;
            }

            first = Vector3.Normalize(first);
            var second = Vector3.Cross(axis, first);
            var inverse = 1.0 / ((double)scale * scale);

            var normal = new double[4, 5];
            foreach (var i in band)
            {
                var sample = samples[i];
                double u = Vector3.Dot(sample.Local, first);
                double v = Vector3.Dot(sample.Local, second);
                double g = sample.Gap + (taper * Vector3.Dot(sample.Local, axis));
                double[] row = [2 * u, 2 * v, 2 * g * inverse, -1.0];
                var rhs = (u * u) + (v * v) - (g * g * inverse);

                for (var r = 0; r < 4; r++)
                {
                    for (var c = 0; c < 4; c++)
                    {
                        normal[r, c] += row[r] * row[c];
                    }

                    normal[r, 4] += row[r] * rhs;
                }
            }

            if (!SolveInPlace(normal, out var solution))
            {
                return false;
            }

            centre = ((float)solution[0] * first) + ((float)solution[1] * second);
            beta = (float)solution[2];
            return IsFinite(centre) && float.IsFinite(beta);
        }

        // Gaussian elimination with partial pivoting on a 4x5 augmented matrix.
        static bool SolveInPlace(double[,] matrix, out double[] solution)
        {
            solution = new double[4];
            for (var column = 0; column < 4; column++)
            {
                var pivot = column;
                for (var row = column + 1; row < 4; row++)
                {
                    if (Math.Abs(matrix[row, column]) > Math.Abs(matrix[pivot, column]))
                    {
                        pivot = row;
                    }
                }

                if (Math.Abs(matrix[pivot, column]) < 1e-12)
                {
                    return false;
                }

                if (pivot != column)
                {
                    for (var c = column; c < 5; c++)
                    {
                        (matrix[column, c], matrix[pivot, c]) = (matrix[pivot, c], matrix[column, c]);
                    }
                }

                for (var row = column + 1; row < 4; row++)
                {
                    var factor = matrix[row, column] / matrix[column, column];
                    for (var c = column; c < 5; c++)
                    {
                        matrix[row, c] -= factor * matrix[column, c];
                    }
                }
            }

            for (var row = 3; row >= 0; row--)
            {
                var sum = matrix[row, 4];
                for (var c = row + 1; c < 4; c++)
                {
                    sum -= matrix[row, c] * solution[c];
                }

                solution[row] = sum / matrix[row, row];
            }

            return solution.All(double.IsFinite);
        }

        static bool IsFinite(Vector3 value)
            => float.IsFinite(value.X) && float.IsFinite(value.Y) && float.IsFinite(value.Z);

        // Candidate shapes, kept distinct on a fixed grid so the same capsule reached from several planes is
        // scored once.
        sealed class CandidateSet
        {
            readonly HashSet<(int, int, int, int, int, int, int, int)> seen = [];

            public List<CapsuleFit> Fits { get; } = [];

            public void Add(CapsuleFit fit)
            {
                if (Fits.Count >= PlanarizeCandidateLimit || fit.R0 <= 0f || fit.R1 <= 0f
                    || !float.IsFinite(fit.R0) || !float.IsFinite(fit.R1)
                    || !IsFinite(fit.C0) || !IsFinite(fit.C1))
                {
                    return;
                }

                static int Cell(float value) => (int)MathF.Round(value * PlanarizeCandidateGrid);

                if (seen.Add((Cell(fit.C0.X), Cell(fit.C0.Y), Cell(fit.C0.Z), Cell(fit.R0),
                    Cell(fit.C1.X), Cell(fit.C1.Y), Cell(fit.C1.Z), Cell(fit.R1))))
                {
                    Fits.Add(fit);
                }
            }
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
        /// <para>
        /// Older compiles hold the frame as <c>tmFrame</c>, a 3x4 matrix with the bone-local origin in its
        /// last column, rather than the position-quaternion <c>tmFrame2</c>.
        /// </para>
        /// </summary>
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

            for (var i = 0; i < rigids.Count; i++)
            {
                var rigid = rigids[i];

                // m_SphereRigids entries store a single sphere as a flat vSphere [x,y,z,r] array
                // (unlike m_TaperedCapsuleRigids' vSphere, which nests TWO such arrays for its end-caps),
                // as m_vCenter+m_flRadius, or - in older compiles - as vCenter+flRadius.
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

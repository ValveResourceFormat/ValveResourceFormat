using System.Linq;
using ValveResourceFormat.Serialization.KeyValues;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// A cloth sheet generated over a group of neighbouring bone chains (rows = positions along the
        /// chains, columns = chains plus interpolated columns between them). Mirrors the proxy grids item
        /// authors hand-build for skirts/capes: the sheet simulates the surface between the chains and
        /// drives the render mesh directly.
        /// </summary>
        public sealed class ChainGrid
        {
            /// <summary>Gets the model-space rest position of each grid vertex.</summary>
            public required Vector3[] Positions { get; init; }
            /// <summary>Gets the grid-parameter UVs (u = across chains, v = along them).</summary>
            public required Vector2[] Texcoords { get; init; }
            /// <summary>Gets the (bone, weight) influences of each vertex (up to 4, bilinear over the chains).</summary>
            public required (string Bone, float Weight)[][] SkinInfluences { get; init; }
            /// <summary>Gets the per-vertex cloth_enable paint (0 = pinned anchor row).</summary>
            public required float[] ClothEnable { get; init; }
            /// <summary>Gets the per-vertex goal strength paint (cbrt of the recovered force attraction).</summary>
            public required float[] GoalStrength { get; init; }
            /// <summary>Gets the per-vertex collision radius paint.</summary>
            public required float[] CollisionRadius { get; init; }
            /// <summary>Gets the per-vertex goal damping paint.</summary>
            public required float[] GoalDamping { get; init; }
            /// <summary>Gets the per-vertex friction paint.</summary>
            public required float[] Friction { get; init; }
            /// <summary>Gets the per-vertex drag paint.</summary>
            public required float[] Drag { get; init; }
            /// <summary>Gets the quads covering the grid.</summary>
            public required List<int[]> Faces { get; init; }
        }

        // How close chain root joints must rest to be considered part of one sheet, in inches.
        const float ChainGridRootDistance = 30f;
        // Interpolated columns inserted between adjacent chains.
        const int ChainGridSubdivisions = 3;

        /// <summary>
        /// Generates cloth sheet grids over groups of neighbouring bone chains. Branched chains are
        /// decomposed into root-to-leaf PATHS (a shared coattail base becomes two columns); paths with
        /// 3+ joints whose roots rest within <see cref="ChainGridRootDistance"/> form one sheet. Returns
        /// an empty list when no group of 2+ paths exists - e.g. cloth made of one isolated strand.
        /// </summary>
        public List<ChainGrid> BuildChainGrids()
        {
            var grids = new List<ChainGrid>();
            var paths = new List<List<BoneChainJoint>>();

            foreach (var chain in BuildBoneChains())
            {
                var byNode = chain.Joints.ToDictionary(j => j.Node);
                var isParent = chain.Joints.Select(j => j.ParentNode).ToHashSet();

                foreach (var leaf in chain.Joints.Where(j => !isParent.Contains(j.Node)))
                {
                    var path = new List<BoneChainJoint>();
                    var current = leaf;
                    while (true)
                    {
                        path.Insert(0, current);
                        if (current.IsRoot || !byNode.TryGetValue(current.ParentNode, out var parent))
                        {
                            break;
                        }

                        current = parent;
                    }

                    if (path.Count >= 3 && path[0].Node < InitPosePositions.Length)
                    {
                        paths.Add(path);
                    }
                }
            }

            if (paths.Count < 2)
            {
                return grids;
            }

            // Union-find style grouping by root rest distance.
            var groupOf = Enumerable.Range(0, paths.Count).ToArray();
            int Find(int x) { while (groupOf[x] != x) { x = groupOf[x] = groupOf[groupOf[x]]; } return x; }
            for (var a = 0; a < paths.Count; a++)
            {
                for (var b = a + 1; b < paths.Count; b++)
                {
                    var da = InitPosePositions[paths[a][0].Node];
                    var db = InitPosePositions[paths[b][0].Node];
                    if (Vector3.Distance(da, db) <= ChainGridRootDistance)
                    {
                        groupOf[Find(a)] = Find(b);
                    }
                }
            }

            foreach (var group in Enumerable.Range(0, paths.Count).GroupBy(Find))
            {
                var members = group.Select(i => paths[i]).ToList();
                if (members.Count < 2)
                {
                    continue;
                }

                grids.Add(BuildGridForChains(members));
            }

            return grids;
        }

        ChainGrid BuildGridForChains(List<List<BoneChainJoint>> members)
        {
            // Order the paths around the centroid of their roots (skirts wrap around the hips).
            var centroid = Vector3.Zero;
            foreach (var path in members)
            {
                centroid += InitPosePositions[path[0].Node];
            }

            centroid /= members.Count;
            members.Sort((a, b) =>
            {
                var pa = InitPosePositions[a[0].Node] - centroid;
                var pb = InitPosePositions[b[0].Node] - centroid;
                return MathF.Atan2(pa.Y, pa.X).CompareTo(MathF.Atan2(pb.Y, pb.X));
            });

            var rows = members.Max(c => c.Count);
            var nodeFriction = Data.GetFloatArray("m_DynNodeFriction");
            float FrictionAt(int node)
            {
                var dynamicIndex = node - StaticNodeCount;
                return dynamicIndex >= 0 && dynamicIndex < nodeFriction.Length
                    ? Math.Clamp(nodeFriction[dynamicIndex], 0f, 1f)
                    : 0f;
            }

            // Sample each chain at uniform arc-length fractions; remember the bracketing joints so the
            // vertex can be skinned/painted by interpolating them.
            var columnSamples = new List<(Vector3 Position, (string Bone, float Weight)[] Influences, float Enable, float Strength, float Radius, float Damping, float Friction, float Drag)[]>();
            foreach (var joints in members)
            {
                var lengths = new float[joints.Count];
                for (var j = 1; j < joints.Count; j++)
                {
                    lengths[j] = lengths[j - 1] + Vector3.Distance(
                        InitPosePositions[joints[j - 1].Node], InitPosePositions[joints[j].Node]);
                }

                var total = MathF.Max(lengths[^1], 1e-4f);
                var samples = new (Vector3, (string, float)[], float, float, float, float, float, float)[rows];
                for (var r = 0; r < rows; r++)
                {
                    var target = total * r / (rows - 1);
                    var j = 1;
                    while (j < joints.Count - 1 && lengths[j] < target) { j++; }
                    var t = Math.Clamp((target - lengths[j - 1]) / MathF.Max(lengths[j] - lengths[j - 1], 1e-4f), 0f, 1f);

                    var a = joints[j - 1];
                    var b = joints[j];
                    var position = Vector3.Lerp(InitPosePositions[a.Node], InitPosePositions[b.Node], t);
                    var influences = t < 1e-3f ? new[] { (a.Name, 1f) }
                        : t > 1f - 1e-3f ? new[] { (b.Name, 1f) }
                        : new[] { (a.Name, 1f - t), (b.Name, t) };

                    var ia = GetIntegrator(a.Node);
                    var ib = GetIntegrator(b.Node);
                    var strength = MathF.Cbrt(Math.Clamp(ia.ForceAttraction + (ib.ForceAttraction - ia.ForceAttraction) * t, 0f, 1f));
                    var radius = GetCollisionRadius(a.Node) + (GetCollisionRadius(b.Node) - GetCollisionRadius(a.Node)) * t;
                    var forceAttraction = ia.ForceAttraction + (ib.ForceAttraction - ia.ForceAttraction) * t;
                    var vertexAttraction = ia.VertexAttraction + (ib.VertexAttraction - ia.VertexAttraction) * t;
                    var damping = GoalDampingFromAttraction(forceAttraction, vertexAttraction);
                    var friction = FrictionAt(a.Node) + (FrictionAt(b.Node) - FrictionAt(a.Node)) * t;
                    var drag = Math.Clamp((ia.PointDamping + (ib.PointDamping - ia.PointDamping) * t) / ClothDragPointDampingScale, 0f, 1f);

                    samples[r] = (position, influences, r == 0 ? 0f : 1f, strength, radius, damping, friction, drag);
                }

                columnSamples.Add(samples);
            }

            // Expand to full columns: each chain column plus interpolated columns between neighbours.
            var columns = new List<(Vector3, (string, float)[], float, float, float, float, float, float)[]>();
            for (var c = 0; c < columnSamples.Count; c++)
            {
                columns.Add(columnSamples[c]);
                if (c == columnSamples.Count - 1)
                {
                    break;
                }

                for (var s = 1; s <= ChainGridSubdivisions; s++)
                {
                    var u = (float)s / (ChainGridSubdivisions + 1);
                    var mid = new (Vector3, (string, float)[], float, float, float, float, float, float)[rows];
                    for (var r = 0; r < rows; r++)
                    {
                        var left = columnSamples[c][r];
                        var right = columnSamples[c + 1][r];
                        var influences = left.Item2.Select(i => (i.Item1, i.Item2 * (1f - u)))
                            .Concat(right.Item2.Select(i => (i.Item1, i.Item2 * u)))
                            .OrderByDescending(i => i.Item2)
                            .Take(4)
                            .ToArray();

                        mid[r] = (
                            Vector3.Lerp(left.Item1, right.Item1, u),
                            influences,
                            r == 0 ? 0f : 1f,
                            left.Item4 + (right.Item4 - left.Item4) * u,
                            left.Item5 + (right.Item5 - left.Item5) * u,
                            left.Item6 + (right.Item6 - left.Item6) * u,
                            left.Item7 + (right.Item7 - left.Item7) * u,
                            left.Item8 + (right.Item8 - left.Item8) * u);
                    }

                    columns.Add(mid);
                }
            }

            var columnCount = columns.Count;
            var positions = new Vector3[columnCount * rows];
            var texcoords = new Vector2[columnCount * rows];
            var skin = new (string Bone, float Weight)[columnCount * rows][];
            var enable = new float[columnCount * rows];
            var strengthArr = new float[columnCount * rows];
            var radiusArr = new float[columnCount * rows];
            var dampingArr = new float[columnCount * rows];
            var frictionArr = new float[columnCount * rows];
            var dragArr = new float[columnCount * rows];

            for (var c = 0; c < columnCount; c++)
            {
                for (var r = 0; r < rows; r++)
                {
                    var v = c * rows + r;
                    var sample = columns[c][r];
                    positions[v] = sample.Item1;
                    skin[v] = [.. sample.Item2.Select(i => (i.Item1, i.Item2))];
                    enable[v] = sample.Item3;
                    strengthArr[v] = sample.Item4;
                    radiusArr[v] = sample.Item5;
                    dampingArr[v] = sample.Item6;
                    frictionArr[v] = sample.Item7;
                    dragArr[v] = sample.Item8;
                    texcoords[v] = new Vector2(columnCount > 1 ? (float)c / (columnCount - 1) : 0f, rows > 1 ? (float)r / (rows - 1) : 0f);
                }
            }

            var faces = new List<int[]>((columnCount - 1) * (rows - 1));
            for (var c = 0; c < columnCount - 1; c++)
            {
                for (var r = 0; r < rows - 1; r++)
                {
                    faces.Add([
                        c * rows + r,
                        (c + 1) * rows + r,
                        (c + 1) * rows + r + 1,
                        c * rows + r + 1,
                    ]);
                }
            }

            return new ChainGrid
            {
                Positions = positions,
                Texcoords = texcoords,
                SkinInfluences = skin,
                ClothEnable = enable,
                GoalStrength = strengthArr,
                CollisionRadius = radiusArr,
                GoalDamping = dampingArr,
                Friction = frictionArr,
                Drag = dragArr,
                Faces = faces,
            };
        }
    }
}

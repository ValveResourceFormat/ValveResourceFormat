using System.Linq;

namespace ValveResourceFormat.ResourceTypes.RubikonPhysics.Softbody
{
    public sealed partial class FeModel
    {
        /// <summary>
        /// Reconstructs sheets for the uncovered proxy nodes that no solve element covers, grouped by rods, source faces and
        /// mesh index, with authored faces where they fit and a triangulation otherwise.
        /// </summary>
        private List<ProxyMesh> BuildProxyMeshesFromRodsOnly(HashSet<int> coveredNodes)
        {
            var result = new List<ProxyMesh>();
            if (InitPosePositions.Length == 0)
            {
                return result;
            }

            var n = CtrlNames.Length;
            var isProxy = new bool[n];
            for (var node = 0; node < n && node < InitPosePositions.Length; node++)
            {
                isProxy[node] = IsProxyNodeName(CtrlNames[node]) && !string.IsNullOrEmpty(CtrlNames[node])
                    && !CtrlNames[node].StartsWith(FreeClothNodePrefix, StringComparison.Ordinal)
                    && !coveredNodes.Contains(node) && !IsHingeRegeneratedProxy(node);
            }

            var parent = new int[n];
            for (var i = 0; i < n; i++)
            {
                parent[i] = i;
            }

            int Find(int x) => FindRoot(parent, x);

            foreach (var rod in Rods)
            {
                if (rod.NodeA < n && rod.NodeB < n
                    && isProxy[rod.NodeA] && isProxy[rod.NodeB])
                {
                    parent[Find(rod.NodeA)] = Find(rod.NodeB);
                }
            }

            foreach (var face in SourceFaces)
            {
                if (SpansProxyMeshes(face))
                {
                    continue;
                }

                var first = -1;
                foreach (var corner in face)
                {
                    if (corner < 0 || corner >= n || !isProxy[corner])
                    {
                        continue;
                    }

                    if (first < 0)
                    {
                        first = corner;
                    }
                    else
                    {
                        parent[Find(corner)] = Find(first);
                    }
                }
            }

            var meshIndexRep = new Dictionary<int, int>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var meshIndex = ParseProxyMeshIndex(CtrlNames[node]);
                if (meshIndex < 0)
                {
                    continue;
                }

                if (meshIndexRep.TryGetValue(meshIndex, out var rep))
                {
                    parent[Find(node)] = Find(rep);
                }
                else
                {
                    meshIndexRep[meshIndex] = node;
                }
            }

            var groups = new Dictionary<int, List<int>>();
            for (var node = 0; node < n; node++)
            {
                if (!isProxy[node])
                {
                    continue;
                }

                var root = Find(node);
                if (!groups.TryGetValue(root, out var nodes))
                {
                    groups[root] = nodes = [];
                }

                nodes.Add(node);
            }

            foreach (var (_, nodeIndices) in groups.OrderBy(static kv => kv.Value.Min()))
            {
                if (nodeIndices.Count < 3)
                {
                    continue;
                }

                var mesh = BuildProxyMeshFromNodeSet(nodeIndices);
                if (mesh is not null)
                {
                    result.Add(mesh);
                }
            }

            return result;
        }

        private ProxyMesh? BuildProxyMeshFromNodeSet(List<int> nodeIndices)
        {
            var sorted = nodeIndices.ToArray();
            SortByAuthoredVertexOrder(sorted);
            nodeIndices = [.. sorted];

            var count = nodeIndices.Count;
            var vertices = ComputeProxyVertexArrays(nodeIndices);
            var positions = vertices.Positions;
            var clothEnable = vertices.ClothEnable;

            var localOf = new Dictionary<int, int>(count);
            for (var i = 0; i < count; i++)
            {
                localOf[nodeIndices[i]] = i;
            }

            var faces = TakeAuthoredFaces(localOf, nodeIndices, out var truncatedTail);
            var usesAuthoredFaces = faces.Count > 0;
            if (!usesAuthoredFaces)
            {
                faces = TriangulateDominantPlane(positions);
                EnsureAllVerticesFaced(positions, faces);
            }

            foreach (var node in usesAuthoredFaces ? truncatedTail : [])
            {
                clothEnable[localOf[node]] = 1f;
            }

            if (faces.Count == 0)
            {
                return null;
            }

            DeclareFacesInStaticNodeOrder(faces, faces.Count, nodeIndices);

            var isDropRisk = !usesAuthoredFaces && ComputeDropRisk(positions, clothEnable, faces);

            return AssembleProxyMesh(vertices, [.. nodeIndices], faces, [], usesAuthoredFaces, isDropRisk,
                usesAuthoredFaces && HasGeneratedClothRoot
                    && vertices.SkinInfluences.All(static v => v.All(static i => IsProxyNodeName(i.Bone))));
        }

        private static Vector2[] ProjectToDominantPlane(Vector3[] positions)
        {
            var min = positions.Aggregate(Vector3.Min);
            var max = positions.Aggregate(Vector3.Max);
            var extent = max - min;
            Span<int> axes = [0, 1, 2];
            axes.Sort((a, b) => extent[b].CompareTo(extent[a]));
            var (axisU, axisV) = (axes[0], axes[1]);

            var projected = new Vector2[positions.Length];
            for (var i = 0; i < positions.Length; i++)
            {
                projected[i] = new Vector2(positions[i][axisU], positions[i][axisV]);
            }

            return projected;
        }

        /// <summary>Adds a triangle to the two nearest non-collinear vertices for each vertex no face covers.</summary>
        private static void EnsureAllVerticesFaced(Vector3[] positions, List<int[]> faces)
        {
            var n = positions.Length;
            if (n < 3)
            {
                return;
            }

            var faced = new HashSet<int>();
            foreach (var face in faces)
            {
                foreach (var v in face)
                {
                    faced.Add(v);
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (faced.Contains(i))
                {
                    continue;
                }

                var ordered = Enumerable.Range(0, n)
                    .Where(j => j != i && positions[j] != positions[i])
                    .OrderBy(j => Vector3.DistanceSquared(positions[i], positions[j]))
                    .ToList();

                if (ordered.Count < 2)
                {
                    continue;
                }

                var a = ordered[0];
                var b = -1;
                for (var k = 1; k < ordered.Count; k++)
                {
                    var cross = Vector3.Cross(positions[a] - positions[i], positions[ordered[k]] - positions[i]);
                    if (cross.LengthSquared() > 1e-6f)
                    {
                        b = ordered[k];
                        break;
                    }
                }

                if (b < 0)
                {
                    continue;
                }

                faces.Add([i, a, b]);
                faced.Add(i);
                faced.Add(a);
                faced.Add(b);
            }
        }

        /// <summary>
        /// Gets whether a pinned vertex has no simulated neighbour, or two vertices lie closer than a quarter of the
        /// median edge.
        /// </summary>
        private static bool ComputeDropRisk(Vector3[] positions, float[] clothEnable, List<int[]> faces)
        {
            var n = positions.Length;
            if (n == 0)
            {
                return false;
            }

            var adjacency = new HashSet<int>[n];
            for (var i = 0; i < n; i++)
            {
                adjacency[i] = [];
            }

            foreach (var face in faces)
            {
                foreach (var a in face)
                {
                    foreach (var b in face)
                    {
                        if (a != b)
                        {
                            adjacency[a].Add(b);
                        }
                    }
                }
            }

            for (var i = 0; i < n; i++)
            {
                if (clothEnable[i] != 0f)
                {
                    continue;
                }

                var hasSimulatedNeighbour = false;
                foreach (var nb in adjacency[i])
                {
                    if (clothEnable[nb] != 0f)
                    {
                        hasSimulatedNeighbour = true;
                        break;
                    }
                }

                if (!hasSimulatedNeighbour)
                {
                    return true;
                }
            }

            var edges = new List<float>();
            foreach (var face in faces)
            {
                for (var a = 0; a < face.Length; a++)
                {
                    var b = (a + 1) % face.Length;
                    edges.Add(Vector3.Distance(positions[face[a]], positions[face[b]]));
                }
            }

            if (edges.Count > 0)
            {
                edges.Sort();
                var weldDistance = edges[edges.Count / 2] * 0.25f;
                for (var i = 0; i < n; i++)
                {
                    for (var j = i + 1; j < n; j++)
                    {
                        if (Vector3.Distance(positions[i], positions[j]) < weldDistance)
                        {
                            return true;
                        }
                    }
                }
            }

            return false;
        }

        /// <summary>Triangulates the positions by Delaunay over their two widest axes.</summary>
        private static List<int[]> TriangulateDominantPlane(Vector3[] positions)
        {
            var faces = new List<int[]>();
            var n = positions.Length;
            if (n < 3)
            {
                return faces;
            }

            var points = ProjectToDominantPlane(positions);

            var min = points.Aggregate(Vector2.Min);
            var max = points.Aggregate(Vector2.Max);
            var center = (min + max) * 0.5f;
            var size = MathF.Max(max.X - min.X, max.Y - min.Y) * 10f + 1f;

            var allPoints = new Vector2[n + 3];
            Array.Copy(points, allPoints, n);
            allPoints[n] = center + new Vector2(0f, size * 2f);
            allPoints[n + 1] = center + new Vector2(-size * 2f, -size);
            allPoints[n + 2] = center + new Vector2(size * 2f, -size);

            var triangles = new List<(int A, int B, int C)> { (n, n + 1, n + 2) };

            for (var p = 0; p < n; p++)
            {
                var bad = triangles.Where(tri => InCircumcircle(allPoints[tri.A], allPoints[tri.B], allPoints[tri.C], allPoints[p])).ToList();

                var polygon = new List<(int A, int B)>();
                foreach (var tri in bad)
                {
                    foreach (var edge in new[] { (tri.A, tri.B), (tri.B, tri.C), (tri.C, tri.A) })
                    {
                        var shared = false;
                        foreach (var other in bad)
                        {
                            if (!other.Equals(tri) && HasEdge(other, edge.Item1, edge.Item2))
                            {
                                shared = true;
                                break;
                            }
                        }

                        if (!shared)
                        {
                            polygon.Add(edge);
                        }
                    }
                }

                triangles.RemoveAll(bad.Contains);
                foreach (var (a, b) in polygon)
                {
                    triangles.Add((a, b, p));
                }
            }

            foreach (var tri in triangles)
            {
                if (tri.A < n && tri.B < n && tri.C < n)
                {
                    faces.Add([tri.A, tri.B, tri.C]);
                }
            }

            return faces;
        }

        private static bool HasEdge((int A, int B, int C) tri, int a, int b)
            => (tri.A == a && tri.B == b) || (tri.A == b && tri.B == a)
            || (tri.B == a && tri.C == b) || (tri.B == b && tri.C == a)
            || (tri.C == a && tri.A == b) || (tri.C == b && tri.A == a);

        private static bool InCircumcircle(Vector2 a, Vector2 b, Vector2 c, Vector2 p)
        {
            var ax = a.X - p.X; var ay = a.Y - p.Y;
            var bx = b.X - p.X; var by = b.Y - p.Y;
            var cx = c.X - p.X; var cy = c.Y - p.Y;

            var det =
                (ax * ax + ay * ay) * (bx * cy - cx * by) -
                (bx * bx + by * by) * (ax * cy - cx * ay) +
                (cx * cx + cy * cy) * (ax * by - bx * ay);

            var area = (b.X - a.X) * (c.Y - a.Y) - (c.X - a.X) * (b.Y - a.Y);
            return area >= 0 ? det > 0 : det < 0;
        }
    }
}

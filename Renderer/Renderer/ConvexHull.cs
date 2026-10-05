namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Finds the edges of the convex hull of an unordered point set.
    /// </summary>
    internal static class ConvexHull
    {
        private record struct Face(int A, int B, int C, Vector3 Normal, float Offset)
        {
            public bool Alive = true;
        }

        /// <summary>
        /// Appends the crease edges of the hull of <paramref name="points"/> to <paramref name="edges"/> as index
        /// pairs. Edges between coplanar faces are skipped, and a planar point set yields its outline.
        /// </summary>
        public static void GetEdges(ReadOnlySpan<Vector3> points, List<int> edges)
        {
            if (points.Length < 3)
            {
                return;
            }

            var centroid = Vector3.Zero;
            var min = new Vector3(float.MaxValue);
            var max = new Vector3(float.MinValue);
            foreach (var point in points)
            {
                centroid += point;
                min = Vector3.Min(min, point);
                max = Vector3.Max(max, point);
            }

            centroid /= points.Length;

            var local = new Vector3[points.Length + 1];
            for (var i = 0; i < points.Length; i++)
            {
                local[i] = points[i] - centroid;
            }

            var size = max - min;
            var extent = MathF.Max(size.MaxComponent(), 1f);
            var epsilon = extent * 1e-4f;
            var count = points.Length;

            var i0 = 0;
            var i1 = Farthest(local, count, p => Vector3.DistanceSquared(p, local[i0]));
            var axis = local[i1] - local[i0];
            var i2 = Farthest(local, count, p => Vector3.Cross(axis, p - local[i0]).LengthSquared());
            var planeNormal = Vector3.Cross(axis, local[i2] - local[i0]);

            if (planeNormal.Length() <= epsilon * axis.Length())
            {
                return;
            }

            planeNormal = Vector3.Normalize(planeNormal);
            var i3 = Farthest(local, count, p => MathF.Abs(Vector3.Dot(planeNormal, p - local[i0])));

            var apex = -1;
            if (MathF.Abs(Vector3.Dot(planeNormal, local[i3] - local[i0])) <= epsilon)
            {
                apex = count;
                local[apex] = local[i0] + planeNormal * extent;
                i3 = apex;
                count++;
            }

            if (Vector3.Dot(planeNormal, local[i3] - local[i0]) > 0)
            {
                (i1, i2) = (i2, i1);
            }

            var faces = new List<Face>(32);

            void AddFace(int a, int b, int c)
            {
                var normal = Vector3.Normalize(Vector3.Cross(local[b] - local[a], local[c] - local[a]));
                faces.Add(new Face(a, b, c, normal, Vector3.Dot(normal, local[a])));
            }

            AddFace(i0, i1, i2);
            AddFace(i0, i3, i1);
            AddFace(i1, i3, i2);
            AddFace(i2, i3, i0);

            var visibleEdges = new HashSet<(int, int)>();

            for (var p = 0; p < points.Length; p++)
            {
                visibleEdges.Clear();

                for (var f = 0; f < faces.Count; f++)
                {
                    var face = faces[f];
                    if (face.Alive && Vector3.Dot(face.Normal, local[p]) - face.Offset > epsilon)
                    {
                        visibleEdges.Add((face.A, face.B));
                        visibleEdges.Add((face.B, face.C));
                        visibleEdges.Add((face.C, face.A));
                        faces[f] = face with { Alive = false };
                    }
                }

                foreach (var (a, b) in visibleEdges)
                {
                    if (!visibleEdges.Contains((b, a)))
                    {
                        AddFace(a, b, p);
                    }
                }
            }

            var edgeNormals = new Dictionary<(int, int), Vector3>();

            foreach (var face in faces)
            {
                if (!face.Alive)
                {
                    continue;
                }

                foreach (var (a, b) in (ReadOnlySpan<(int, int)>)[(face.A, face.B), (face.B, face.C), (face.C, face.A)])
                {
                    var key = a < b ? (a, b) : (b, a);

                    if (!edgeNormals.Remove(key, out var otherNormal))
                    {
                        edgeNormals.Add(key, face.Normal);
                    }
                    else if (a != apex && b != apex && Vector3.Dot(face.Normal, otherNormal) < 0.999f)
                    {
                        edges.Add(a);
                        edges.Add(b);
                    }
                }
            }
        }

        private static int Farthest(Vector3[] points, int count, Func<Vector3, float> distance)
        {
            var best = 0;
            for (var i = 1; i < count; i++)
            {
                if (distance(points[i]) > distance(points[best]))
                {
                    best = i;
                }
            }

            return best;
        }
    }
}

namespace ValveResourceFormat.Renderer.Utils
{
    /// <summary>
    /// Finds the edges of the convex hull of an unordered point set. An instance keeps its working buffers between
    /// calls, so one instance can serve many hulls, but only from one thread at a time.
    /// </summary>
    internal sealed class ConvexHull
    {
        private record struct Face(int A, int B, int C, Vector3 Normal, float Offset)
        {
            public bool Alive = true;
        }

        private readonly List<Face> faces = new(32);
        private readonly HashSet<(int, int)> visibleEdges = [];
        private readonly Dictionary<(int, int), Vector3> edgeNormals = [];
        private Vector3[] local = [];

        /// <summary>
        /// Gets the most edges the hull of <paramref name="pointCount"/> points has, for sizing buffers: a convex
        /// polyhedron with V vertices has at most 3V - 6 edges.
        /// </summary>
        public static int GetMaxEdgeCount(int pointCount) => pointCount < 3 ? 0 : 3 * pointCount - 6;

        /// <summary>
        /// Appends the crease edges of the hull of <paramref name="points"/> to <paramref name="edges"/> as index
        /// pairs. Edges between coplanar faces are skipped, and a planar point set yields its outline.
        /// </summary>
        public void GetEdges(ReadOnlySpan<Vector3> points, List<int> edges)
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

            // One spare slot for the apex that closes a planar point set
            if (local.Length < points.Length + 1)
            {
                local = new Vector3[points.Length + 1];
            }

            for (var i = 0; i < points.Length; i++)
            {
                local[i] = points[i] - centroid;
            }

            var size = max - min;
            var extent = MathF.Max(size.MaxComponent(), 1f);
            var epsilon = extent * 1e-4f;
            var count = points.Length;

            var i0 = 0;
            var i1 = FarthestFromPoint(local, count, local[i0]);
            var axis = local[i1] - local[i0];
            var i2 = FarthestFromLine(local, count, local[i0], axis);
            var planeNormal = Vector3.Cross(axis, local[i2] - local[i0]);

            if (planeNormal.Length() <= epsilon * axis.Length())
            {
                return;
            }

            planeNormal = Vector3.Normalize(planeNormal);
            var i3 = FarthestFromPlane(local, count, local[i0], planeNormal);

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

            faces.Clear();

            AddFace(i0, i1, i2);
            AddFace(i0, i3, i1);
            AddFace(i1, i3, i2);
            AddFace(i2, i3, i0);

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

            edgeNormals.Clear();

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

        private void AddFace(int a, int b, int c)
        {
            var normal = Vector3.Normalize(Vector3.Cross(local[b] - local[a], local[c] - local[a]));
            faces.Add(new Face(a, b, c, normal, Vector3.Dot(normal, local[a])));
        }

        private static int FarthestFromPoint(Vector3[] points, int count, Vector3 origin)
        {
            var best = 0;
            var bestDistance = Vector3.DistanceSquared(points[0], origin);

            for (var i = 1; i < count; i++)
            {
                var distance = Vector3.DistanceSquared(points[i], origin);

                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int FarthestFromLine(Vector3[] points, int count, Vector3 origin, Vector3 direction)
        {
            var best = 0;
            var bestDistance = Vector3.Cross(direction, points[0] - origin).LengthSquared();

            for (var i = 1; i < count; i++)
            {
                var distance = Vector3.Cross(direction, points[i] - origin).LengthSquared();

                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }

        private static int FarthestFromPlane(Vector3[] points, int count, Vector3 origin, Vector3 normal)
        {
            var best = 0;
            var bestDistance = MathF.Abs(Vector3.Dot(normal, points[0] - origin));

            for (var i = 1; i < count; i++)
            {
                var distance = MathF.Abs(Vector3.Dot(normal, points[i] - origin));

                if (distance > bestDistance)
                {
                    best = i;
                    bestDistance = distance;
                }
            }

            return best;
        }
    }
}

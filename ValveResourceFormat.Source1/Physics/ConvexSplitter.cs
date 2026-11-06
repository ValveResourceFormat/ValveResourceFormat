using System.Linq;

namespace ValveResourceFormat.Source1.Physics;

/// <summary>
/// Cuts a closed convex triangle surface in two along a plane. Each piece is closed by a cap in the plane and is
/// convex, so the pieces together fill exactly the shape of the original.
/// </summary>
internal static class ConvexSplitter
{
    // Points closer than this to the cutting plane lie on it, in inches
    private const float PlaneEpsilon = 0.01f;

    // A cap triangle with a doubled area below this is degenerate
    private const float MinCapCrossSquared = 1e-6f;

    internal sealed record Piece(List<Vector3> Points, List<int> Triangles);

    /// <summary>
    /// Cuts the surface through the median of its points along each axis and keeps the cut whose larger piece has the
    /// fewest triangles. The cap adds points, so a piece of a finely tessellated surface can have as many triangles as
    /// the surface, and is cut again.
    /// </summary>
    /// <param name="points">The corners of the surface.</param>
    /// <param name="triangles">Three indices into <paramref name="points"/> per triangle.</param>
    /// <param name="lower">The piece below the plane.</param>
    /// <param name="upper">The piece above the plane.</param>
    /// <returns>Whether the surface was cut.</returns>
    public static bool TrySplit(IReadOnlyList<Vector3> points, IReadOnlyList<int> triangles, out Piece lower, out Piece upper)
    {
        lower = new Piece([], []);
        upper = new Piece([], []);

        var corners = PhysicsAggregateBuilder.GetOutwardTriangles(points, triangles);
        var min = new Vector3(float.MaxValue);
        var max = new Vector3(float.MinValue);

        foreach (var point in points)
        {
            min = Vector3.Min(min, point);
            max = Vector3.Max(max, point);
        }

        var extent = max - min;
        var axes = new[] { 0, 1, 2 };
        Array.Sort(axes, (a, b) => extent[b].CompareTo(extent[a]));

        var isCut = false;
        var smallestLargerPiece = int.MaxValue;

        foreach (var axis in axes)
        {
            var normal = Vector3.Zero;
            normal[axis] = 1f;

            // The median can lie on a flat face, which leaves nothing on one side of the plane
            var offset = GetMedian(points, axis);
            var isAxisCut = TryCut(points, corners, normal, offset, out var axisLower, out var axisUpper);

            if (!isAxisCut)
            {
                offset = (min[axis] + max[axis]) * 0.5f;
                isAxisCut = TryCut(points, corners, normal, offset, out axisLower, out axisUpper);
            }

            if (!isAxisCut)
            {
                continue;
            }

            var largerPiece = Math.Max(axisLower.Triangles.Count, axisUpper.Triangles.Count);

            if (largerPiece >= smallestLargerPiece)
            {
                continue;
            }

            smallestLargerPiece = largerPiece;
            lower = axisLower;
            upper = axisUpper;
            isCut = true;
        }

        return isCut;
    }

    private static float GetMedian(IReadOnlyList<Vector3> points, int axis)
    {
        var values = new float[points.Count];

        for (var i = 0; i < values.Length; i++)
        {
            values[i] = points[i][axis];
        }

        Array.Sort(values);

        return (values[(values.Length - 1) / 2] + values[values.Length / 2]) * 0.5f;
    }

    private static bool TryCut(IReadOnlyList<Vector3> points, int[] corners, Vector3 normal, float offset, out Piece lower, out Piece upper)
    {
        lower = new Piece([], []);
        upper = new Piece([], []);

        var cutPoints = new List<Vector3>(points);
        var sides = new List<int>(points.Count);
        var crossings = new Dictionary<(int, int), int>();

        foreach (var point in points)
        {
            var distance = Vector3.Dot(normal, point) - offset;
            var side = 0;

            if (distance > PlaneEpsilon)
            {
                side = 1;
            }
            else if (distance < -PlaneEpsilon)
            {
                side = -1;
            }

            sides.Add(side);
        }

        if (!sides.Contains(-1) || !sides.Contains(1))
        {
            return false;
        }

        var lowerTriangles = new List<int>();
        var upperTriangles = new List<int>();
        var polygon = new List<int>(4);
        var planeEdges = new Dictionary<int, int>();

        for (var i = 0; i < corners.Length; i += 3)
        {
            var triangle = corners.AsSpan(i, 3);

            ClipTriangle(triangle, -1);

            if (!AddPlaneEdges())
            {
                return false;
            }

            AddFan(lowerTriangles, polygon);

            ClipTriangle(triangle, 1);
            AddFan(upperTriangles, polygon);
        }

        var cap = GetCap(cutPoints, planeEdges);

        if (cap == null)
        {
            return false;
        }

        AddCap(lowerTriangles, cap, reverse: false);
        AddCap(upperTriangles, cap, reverse: true);

        lower = Compact(cutPoints, lowerTriangles);
        upper = Compact(cutPoints, upperTriangles);

        return lower.Points.Count >= 4 && upper.Points.Count >= 4;

        // Keeps the part of a triangle on one side of the plane and on it, as a polygon of point indices
        void ClipTriangle(ReadOnlySpan<int> triangle, int keep)
        {
            polygon.Clear();

            var onPlane = 0;

            for (var corner = 0; corner < 3; corner++)
            {
                var current = triangle[corner];
                var next = triangle[(corner + 1) % 3];
                var currentSide = sides[current];
                var nextSide = sides[next];

                if (currentSide == 0)
                {
                    onPlane++;
                }

                if (currentSide == keep || currentSide == 0)
                {
                    polygon.Add(current);
                }

                if (currentSide * nextSide < 0)
                {
                    polygon.Add(GetCrossing(current, next));
                }
            }

            // A triangle lying in the plane belongs to the cap
            if (onPlane == 3)
            {
                polygon.Clear();
            }
        }

        // The edges of the clipped polygon that lie in the plane are the boundary of the cap
        bool AddPlaneEdges()
        {
            if (polygon.Count < 3)
            {
                return true;
            }

            for (var corner = 0; corner < polygon.Count; corner++)
            {
                var from = polygon[corner];
                var to = polygon[(corner + 1) % polygon.Count];

                if (sides[from] == 0 && sides[to] == 0 && !planeEdges.TryAdd(from, to))
                {
                    return false;
                }
            }

            return true;
        }

        int GetCrossing(int from, int to)
        {
            var key = from < to ? (from, to) : (to, from);

            if (crossings.TryGetValue(key, out var index))
            {
                return index;
            }

            var fromDistance = Vector3.Dot(normal, cutPoints[from]) - offset;
            var toDistance = Vector3.Dot(normal, cutPoints[to]) - offset;
            var amount = fromDistance / (fromDistance - toDistance);

            index = cutPoints.Count;
            cutPoints.Add(Vector3.Lerp(cutPoints[from], cutPoints[to], amount));
            sides.Add(0);
            crossings.Add(key, index);

            return index;
        }
    }

    private static void AddFan(List<int> triangles, List<int> polygon)
    {
        for (var i = 1; i < polygon.Count - 1; i++)
        {
            triangles.Add(polygon[0]);
            triangles.Add(polygon[i]);
            triangles.Add(polygon[i + 1]);
        }
    }

    // Follows the edges that the lower triangles have in the plane around the cut, which runs the opposite way to the
    // cap, and rotates the cap to start at the point whose fan has the largest smallest triangle. Returns null when the
    // edges are not one loop or every fan has a degenerate triangle.
    private static int[]? GetCap(List<Vector3> points, Dictionary<int, int> planeEdges)
    {
        if (planeEdges.Count < 3)
        {
            return null;
        }

        var sorted = new int[planeEdges.Count];
        var first = planeEdges.Keys.First();
        var current = first;

        for (var i = sorted.Length - 1; i >= 0; i--)
        {
            sorted[i] = current;

            if (!planeEdges.TryGetValue(current, out current) || (current == first && i > 0))
            {
                return null;
            }
        }

        if (current != first)
        {
            return null;
        }

        var bestStart = 0;
        var bestQuality = 0f;

        for (var start = 0; start < sorted.Length; start++)
        {
            var quality = GetFanQuality(points, sorted, start);

            if (quality > bestQuality)
            {
                bestStart = start;
                bestQuality = quality;
            }
        }

        if (bestQuality < MinCapCrossSquared)
        {
            return null;
        }

        var rotated = new int[sorted.Length];

        for (var i = 0; i < rotated.Length; i++)
        {
            rotated[i] = sorted[(bestStart + i) % sorted.Length];
        }

        return rotated;
    }

    // The smallest doubled triangle area squared of a fan from one point of the cap
    private static float GetFanQuality(List<Vector3> points, int[] cap, int start)
    {
        var apex = points[cap[start]];
        var quality = float.MaxValue;

        for (var i = 1; i < cap.Length - 1; i++)
        {
            var first = points[cap[(start + i) % cap.Length]];
            var second = points[cap[(start + i + 1) % cap.Length]];

            quality = MathF.Min(quality, Vector3.Cross(first - apex, second - apex).LengthSquared());
        }

        return quality;
    }

    // The cap runs counterclockwise around the plane normal, which faces out of the lower piece and into the upper
    private static void AddCap(List<int> triangles, int[] cap, bool reverse)
    {
        for (var i = 1; i < cap.Length - 1; i++)
        {
            triangles.Add(cap[0]);
            triangles.Add(reverse ? cap[i + 1] : cap[i]);
            triangles.Add(reverse ? cap[i] : cap[i + 1]);
        }
    }

    private static Piece Compact(List<Vector3> points, List<int> triangles)
    {
        var indices = new Dictionary<int, int>();
        var pieceTriangles = new List<int>(triangles.Count);
        var piecePoints = new List<Vector3>();

        foreach (var id in triangles)
        {
            if (!indices.TryGetValue(id, out var index))
            {
                index = piecePoints.Count;
                indices.Add(id, index);
                piecePoints.Add(points[id]);
            }

            pieceTriangles.Add(index);
        }

        return new Piece(piecePoints, pieceTriangles);
    }
}

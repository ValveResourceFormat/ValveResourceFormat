namespace ValveResourceFormat.Utils;

/// <summary>
/// Helpers for building triangle meshes.
/// </summary>
public static class MeshUtils
{
    // A corner turning less than this, as twice the area of its triangle, counts as flat
    private const float AreaTolerance = 0.0000001f;

    // Polygons with more corners than this take their working memory from the heap
    private const int MaxCornersOnStack = 128;

    /// <summary>
    /// Triangulates a polygon the way Hammer does: a quad is split along the diagonal that suits it better,
    /// and anything else has its ears cut off one at a time, always the best shaped one that is left.
    /// Triangles keep the winding of the polygon. A polygon no ear can be cut from gets no triangles.
    /// </summary>
    /// <param name="positions">The corners of the polygon, in order.</param>
    /// <param name="normal">The way the polygon faces.</param>
    /// <param name="firstIndex">Index the first corner has in the mesh, the others following it.</param>
    /// <param name="indices">Receives three indices for each triangle.</param>
    public static void TriangulatePolygon(ReadOnlySpan<Vector3> positions, Vector3 normal, int firstIndex, List<int> indices)
    {
        ArgumentNullException.ThrowIfNull(indices);

        var count = positions.Length;

        if (count < 3)
        {
            return;
        }

        if (count == 4)
        {
            TriangulateQuad(positions, normal, firstIndex, indices);
            return;
        }

        var start = indices.Count;
        Span<int> remaining = count <= MaxCornersOnStack ? stackalloc int[count] : new int[count];
        Span<float> scores = count <= MaxCornersOnStack ? stackalloc float[count] : new float[count];

        for (var i = 0; i < count; i++)
        {
            remaining[i] = i;
        }

        for (var i = 0; i < count; i++)
        {
            scores[i] = ComputeEarScore(positions, remaining, i, normal);
        }

        while (count > 2)
        {
            var best = -1;
            var bestScore = 0f;

            for (var i = 0; i < count; i++)
            {
                if (scores[i] > bestScore)
                {
                    best = i;
                    bestScore = scores[i];
                }
            }

            if (best < 0)
            {
                indices.RemoveRange(start, indices.Count - start);
                return;
            }

            indices.Add(firstIndex + remaining[(best + count - 1) % count]);
            indices.Add(firstIndex + remaining[best]);
            indices.Add(firstIndex + remaining[(best + 1) % count]);

            remaining[(best + 1)..count].CopyTo(remaining[best..]);
            scores[(best + 1)..count].CopyTo(scores[best..]);
            count--;

            if (count < 3)
            {
                break;
            }

            // Only the two corners next to the cut ear changed shape
            var previous = (best + count - 1) % count;
            var next = best % count;

            scores[previous] = ComputeEarScore(positions, remaining[..count], previous, normal);
            scores[next] = ComputeEarScore(positions, remaining[..count], next, normal);
        }
    }

    // A quad is split along the diagonal that leaves both triangles facing the way the face does, and
    // along the shorter one when both diagonals do
    private static void TriangulateQuad(ReadOnlySpan<Vector3> positions, Vector3 normal, int firstIndex, List<int> indices)
    {
        const float LongerDiagonalRatio = 1.01f;

        var (lengthA, qualityA) = EvaluateQuadSplit(positions, [0, 1, 2, 0, 2, 3], 0, 2, normal);
        var (lengthB, qualityB) = EvaluateQuadSplit(positions, [0, 1, 3, 1, 2, 3], 1, 3, normal);

        if (qualityA == 0 || qualityB == 0)
        {
            if (qualityA == 0 && qualityB == 0)
            {
                return;
            }

            // One split is flat, which a corner lying on the line of its neighbours causes
            for (var i = 0; i < 4; i++)
            {
                var point = positions[i];
                var a = positions[(i + 1) % 4];
                var b = positions[(i + 2) % 4];
                var c = positions[(i + 3) % 4];

                if (DistanceToSegment(point, a, b) < AreaTolerance || DistanceToSegment(point, b, c) < AreaTolerance)
                {
                    return;
                }
            }
        }

        ReadOnlySpan<int> split = qualityB > qualityA || lengthA > lengthB * LongerDiagonalRatio
            ? [0, 1, 3, 1, 2, 3]
            : [0, 1, 2, 0, 2, 3];

        foreach (var index in split)
        {
            indices.Add(firstIndex + index);
        }
    }

    // The squared length of the diagonal, and 0 for a split with a flat triangle, 2 for one with a
    // triangle facing away from the face, 3 for one with both triangles facing the way the face does
    private static (float DiagonalLengthSquared, int Quality) EvaluateQuadSplit(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> triangles, int diagonalStart, int diagonalEnd, Vector3 normal)
    {
        const float DegenerateTolerance = 1e-14f;

        var first = Vector3.Cross(positions[triangles[1]] - positions[triangles[0]], positions[triangles[2]] - positions[triangles[0]]);
        var second = Vector3.Cross(positions[triangles[4]] - positions[triangles[3]], positions[triangles[5]] - positions[triangles[3]]);
        var diagonal = (positions[diagonalStart] - positions[diagonalEnd]).LengthSquared();

        if (first.LengthSquared() < DegenerateTolerance || second.LengthSquared() < DegenerateTolerance || diagonal < DegenerateTolerance)
        {
            return (0f, 0);
        }

        var facesNormal = Vector3.Dot(normal, first) > 0f && Vector3.Dot(normal, second) > 0f;

        return (diagonal, facesNormal ? 3 : 2);
    }

    // Negative for a corner that is no ear: one that is reflex or flat, or whose triangle holds another
    // corner of the face. Otherwise higher the further its triangle is from a sliver, and slightly
    // higher the smaller the corners around it are.
    private static float ComputeEarScore(ReadOnlySpan<Vector3> positions, ReadOnlySpan<int> remaining, int corner, Vector3 normal)
    {
        const float NotAnEar = -1f;

        var count = remaining.Length;
        var previousIndex = (corner + count - 1) % count;
        var nextIndex = (corner + 1) % count;
        var previous = positions[remaining[previousIndex]];
        var current = positions[remaining[corner]];
        var next = positions[remaining[nextIndex]];

        var area = Vector3.Dot(normal, Vector3.Cross(current - previous, next - previous));

        if (area < AreaTolerance)
        {
            return NotAnEar;
        }

        for (var i = 0; i < count; i++)
        {
            if (i == previousIndex || i == corner || i == nextIndex)
            {
                continue;
            }

            var point = positions[remaining[i]];

            // A corner at the very spot of one of the three does not block the ear
            if (IsPointInTriangle(previous, current, next, point, normal) && point != previous && point != current && point != next)
            {
                return NotAnEar;
            }
        }

        if (count > 3)
        {
            var afterNext = positions[remaining[(corner + 2) % count]];
            area = MathF.Min(area, Vector3.Dot(normal, Vector3.Cross(current - afterNext, next - afterNext)));
        }

        if (count > 4)
        {
            var beforePrevious = positions[remaining[(corner + count - 2) % count]];
            area = MathF.Min(area, Vector3.Dot(normal, Vector3.Cross(previous - beforePrevious, current - beforePrevious)));
        }

        var incoming = MathUtils.SafeNormalize(current - previous);
        var outgoing = MathUtils.SafeNormalize(next - current);
        var across = MathUtils.SafeNormalize(next - previous);
        var sharpest = MathUtils.Max(Vector3.Dot(across, incoming), Vector3.Dot(outgoing, incoming), Vector3.Dot(outgoing, across));

        return (1f / MathF.Max(area, 1f)) + (2f * (1f - sharpest));
    }

    private static bool IsPointInTriangle(Vector3 a, Vector3 b, Vector3 c, Vector3 point, Vector3 normal)
    {
        const float OnEdgeToleranceSquared = 1e-14f;

        if (Vector3.Dot(normal, Vector3.Cross(c - b, point - b)) > -AreaTolerance
            && Vector3.Dot(normal, Vector3.Cross(a - c, point - c)) > -AreaTolerance
            && Vector3.Dot(normal, Vector3.Cross(b - a, point - a)) > -AreaTolerance)
        {
            return true;
        }

        return IsOnSegment(point, a, b) || IsOnSegment(point, b, c) || IsOnSegment(point, c, a);

        static bool IsOnSegment(Vector3 point, Vector3 start, Vector3 end)
        {
            var fraction = FractionAlongLine(point, start, end);

            return (point - Vector3.Lerp(start, end, fraction)).LengthSquared() < OnEdgeToleranceSquared && fraction is >= 0f and <= 1f;
        }
    }

    // Where along the line through two points a point is closest to it, 0 at the start and 1 at the end
    private static float FractionAlongLine(Vector3 point, Vector3 start, Vector3 end)
    {
        const float ShortestLineSquared = 0.00001f;

        var direction = end - start;
        var lengthSquared = direction.LengthSquared();

        return lengthSquared >= ShortestLineSquared ? Vector3.Dot(direction, point - start) / lengthSquared : 0f;
    }

    private static float DistanceToSegment(Vector3 point, Vector3 start, Vector3 end)
    {
        var fraction = Math.Clamp(FractionAlongLine(point, start, end), 0f, 1f);

        return Vector3.Distance(point, Vector3.Lerp(start, end, fraction));
    }
}

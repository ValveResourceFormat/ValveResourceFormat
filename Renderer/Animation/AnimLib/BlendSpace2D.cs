using System.Diagnostics;

namespace ValveResourceFormat.Renderer.AnimLib;

/// <summary>
/// Finds what a point in a triangulated 2D blend space blends between: the corners of the triangle it is
/// in, or the nearest edge of the hull around the triangles when it is outside them all.
/// </summary>
public static class BlendSpace2D
{
    /// <summary>Up to three sources and the weights to blend them with, the first two and then the third.</summary>
    public struct Result
    {
        /// <summary>The index of the first source, or -1 for none.</summary>
        public int Src0 { get; set; }
        /// <summary>The index of the second source, or -1 for none.</summary>
        public int Src1 { get; set; }
        /// <summary>The index of the third source, or -1 for none.</summary>
        public int Src2 { get; set; }
        /// <summary>How far to blend from the first source to the second.</summary>
        public float Weight01 { get; set; }
        /// <summary>How far to blend from that blend to the third source.</summary>
        public float Weight12 { get; set; }

        /// <summary>Clears the sources and weights.</summary>
        public void Reset()
        {
            Src0 = Src1 = Src2 = -1;
            Weight01 = Weight12 = 0f;
        }
    }

    /// <summary>Calculates the sources and weights for a point.</summary>
    /// <param name="points">The position of each source in the blend space.</param>
    /// <param name="indices">The triangles over the points, three indices each.</param>
    /// <param name="hullIndices">The closed outline around the triangles, as point indices.</param>
    /// <param name="point">The point to blend at.</param>
    /// <param name="result">Receives the sources and weights.</param>
    public static void CalculateWeights(Vector2[] points, uint[] indices, uint[] hullIndices, Vector2 point, ref Result result)
    {
        result.Reset();

        var enclosingTriangleFound = false;
        for (var i = 0; i + 2 < indices.Length; i += 3)
        {
            int i0 = (int)indices[i];
            int i1 = (int)indices[i + 1];
            int i2 = (int)indices[i + 2];

            if (CalculateBarycentricCoordinates(point, points[i0], points[i1], points[i2], out var bcc))
            {
                // Sort the three contributions ascending by weight (allocation-free)
                Span<(int Idx, float Weight)> iw =
                [
                    (i0, bcc.X),
                    (i1, bcc.Y),
                    (i2, bcc.Z),
                ];

                if (iw[0].Weight > iw[1].Weight)
                {
                    (iw[0], iw[1]) = (iw[1], iw[0]);
                }

                if (iw[1].Weight > iw[2].Weight)
                {
                    (iw[1], iw[2]) = (iw[2], iw[1]);
                }

                if (iw[0].Weight > iw[1].Weight)
                {
                    (iw[0], iw[1]) = (iw[1], iw[0]);
                }

                if (IsNearEqual(iw[2].Weight, 1f, 1e-4f))
                {
                    result.Src0 = iw[2].Idx;
                    result.Src1 = result.Src2 = -1;
                    result.Weight01 = result.Weight12 = 0f;
                }
                else
                {
                    result.Src0 = iw[0].Idx; // lowest
                    result.Src1 = iw[1].Idx;
                    result.Src2 = iw[2].Idx; // highest
                    result.Weight01 = iw[1].Weight / (iw[0].Weight + iw[1].Weight);
                    result.Weight12 = iw[2].Weight;
                }

                enclosingTriangleFound = true;
                break;
            }
        }

        if (!enclosingTriangleFound)
        {
            // Find the nearest hull edge and project onto it (hull has its first index duplicated at the end)
            var closestDistance = float.MaxValue;
            var closestStartHullIdx = -1;
            var closestT = 0f;

            for (var i = 1; i < hullIndices.Length; i++)
            {
                var p0 = points[hullIndices[i - 1]];
                var p1 = points[hullIndices[i]];
                var cp = ClosestPointOnSegment(p0, p1, point, out var t);
                var distance = Vector2.DistanceSquared(cp, point);
                if (distance < closestDistance)
                {
                    closestDistance = distance;
                    closestStartHullIdx = i - 1;
                    closestT = t;
                }
            }

            Debug.Assert(closestStartHullIdx >= 0);
            result.Src0 = (int)hullIndices[closestStartHullIdx];
            result.Src1 = (int)hullIndices[closestStartHullIdx + 1];
            result.Src2 = -1;
            result.Weight01 = closestT;
            result.Weight12 = 0f;
        }

        Debug.Assert(result.Src0 != -1);

        // Simplify away redundant blends
        if (IsNearEqual(result.Weight01, 1f, 1e-4f))
        {
            result.Src0 = result.Src1;
            result.Src1 = result.Src2;
            result.Src2 = -1;
            result.Weight01 = result.Weight12;
            result.Weight12 = 0f;
        }
        else if (result.Weight01 < 1e-4f)
        {
            if (result.Src2 == -1)
            {
                result.Src1 = -1;
            }
            else
            {
                result.Src0 = result.Src1;
                result.Src1 = result.Src2;
                result.Src2 = -1;
                result.Weight01 = result.Weight12;
                result.Weight12 = 0f;
            }
        }
    }

    private static bool IsNearEqual(float a, float b, float epsilon = 1e-5f) => MathF.Abs(a - b) <= epsilon;

    // How far outside a triangle a barycentric weight may round and still count as inside
    private const float TriangleEdgeTolerance = 1e-5f;

    // True when the point is inside the triangle, edges included. A point on an edge shared by two
    // triangles rounds a hair outside both, and without the tolerance it would fall back to the nearest
    // hull edge, a pose from the rim of the blend space, for that one update.
    private static bool CalculateBarycentricCoordinates(Vector2 p, Vector2 a, Vector2 b, Vector2 c, out Vector3 bary)
    {
        var v0 = b - a;
        var v1 = c - a;
        var v2 = p - a;

        var denominator = 1f / ((v0.X * v1.Y) - (v1.X * v0.Y));
        var y = ((v2.X * v1.Y) - (v1.X * v2.Y)) * denominator;
        var z = ((v0.X * v2.Y) - (v2.X * v0.Y)) * denominator;
        bary = new Vector3(1f - y - z, y, z);

        if (!Vector3.GreaterThanOrEqualAll(bary, new Vector3(-TriangleEdgeTolerance)) || !Vector3.LessThanOrEqualAll(bary, new Vector3(1f + TriangleEdgeTolerance)))
        {
            return false;
        }

        // The pairwise blend weights derived from these have to stay within 0 and 1
        bary = Vector3.Max(bary, Vector3.Zero);
        bary /= bary.X + bary.Y + bary.Z;
        return true;
    }

    private static Vector2 ClosestPointOnSegment(Vector2 a, Vector2 b, Vector2 p, out float t)
    {
        var ab = b - a;
        var lengthSq = Vector2.Dot(ab, ab);
        t = lengthSq <= 1e-10f ? 0f : Math.Clamp(Vector2.Dot(p - a, ab) / lengthSq, 0f, 1f);
        return a + ab * t;
    }
}

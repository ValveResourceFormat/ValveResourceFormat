namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

partial class HalfEdgeMesh
{
    /// <summary>
    /// <para> Untriangulation inspired by blender's "Triangles to Quads" operator </para> 
    /// <para> Joins pairs of adjacent triangles into quads by dissolving their shared edge, processing the candidates with the lowest quad error first </para> 
    /// <para> A pair is skipped when the angle between the triangle normals exceeds maxFaceAngleDegrees, or if any of its corners deviates from
    /// 90 degrees by more than maxShapeAngleDegrees </para> 
    /// </summary>
    /// <param name="positions">Mesh Vertex Positions</param>
    /// <param name="canMergeFaces">Function that compares two faces, and returns if the faces should be merged or not</param>
    /// <param name="maxFaceAngleDegrees">Angle between triangle normals to skip this pair</param>
    /// <param name="maxShapeAngleDegrees">Angle deviation of a triangle corner from 90 degrees to skip</param>
    /// <returns></returns>
    public int UntriangulateMesh(VertexData<Vector3> positions,
        Func<FaceHandle, FaceHandle, bool>? canMergeFaces = null,
        float maxFaceAngleDegrees = 40f,
        float maxShapeAngleDegrees = 80f)
    {
        var angleFaceCos = MathF.Cos(maxFaceAngleDegrees * (MathF.PI / 180f));
        var angleShape = maxShapeAngleDegrees * (MathF.PI / 180f);

        // "full" edges considered for removal, and their error
        var candidates = new List<(float Error, HalfEdgeHandle Edge)>();

        foreach (var hEdge in HalfEdgeHandles)
        {
            // visit each full edge once
            if (GetFullEdgeForHalfEdge(hEdge) != hEdge)
                continue;

            var hFaceA = hEdge.Face;
            var hFaceB = hEdge.OppositeEdge.Face;

            if (hFaceA == FaceHandle.Invalid || hFaceB == FaceHandle.Invalid || hFaceA == hFaceB)
                continue;

            // only look at triangles
            if (ComputeNumEdgesInFace(hFaceA) != 3 || ComputeNumEdgesInFace(hFaceB) != 3)
                continue;

            if (canMergeFaces != null && !canMergeFaces(hFaceA, hFaceB))
                continue;

            if (!EdgeShouldBeRemoved(hEdge, positions, angleFaceCos, angleShape))
                continue;

            EdgeToQuadVerts(hEdge, positions, out var v1, out var v2, out var v3, out var v4);
            candidates.Add((QuadCalcError(v1, v2, v3, v4), hEdge));
        }

        // join the best pairs first
        candidates.Sort((x, y) => x.Error.CompareTo(y.Error));

        var mergedFaces = new HashSet<FaceHandle>();
        var dissolvedCount = 0;

        foreach (var (_, hEdge) in candidates)
        {
            var hFaceA = hEdge.Face;
            var hFaceB = hEdge.OppositeEdge.Face;

            if (mergedFaces.Contains(hFaceA) || mergedFaces.Contains(hFaceB))
                continue;

            if (DissolveEdge(hEdge, out _))
            {
                mergedFaces.Add(hFaceA);
                mergedFaces.Add(hFaceB);
                dissolvedCount++;
            }
        }

        return dissolvedCount;
    }

    // the quad ring left behind when the shared edge is dissolved
    // apex of face A, edge start vertex, apex of face B, edge end vertex
    private static void EdgeToQuadVerts(HalfEdgeHandle hEdge, VertexData<Vector3> positions, out Vector3 v1, out Vector3 v2, out Vector3 v3, out Vector3 v4)
    {
        var hOpposite = hEdge.OppositeEdge;

        v1 = positions[hEdge.NextEdge.Vertex];
        v2 = positions[hOpposite.Vertex];
        v3 = positions[hOpposite.NextEdge.Vertex];
        v4 = positions[hEdge.Vertex];
    }

    // check if we pass the angle constraints
    private static bool EdgeShouldBeRemoved(HalfEdgeHandle hEdge, VertexData<Vector3> positions, float angleFaceCos, float angleShape)
    {
        const float HalfPi = MathF.PI / 2f;

        EdgeToQuadVerts(hEdge, positions, out var v1, out var v2, out var v3, out var v4);

        // the triangles must be within the face angle limit of each other
        var normalA = TriangleNormal(v2, v4, v1);
        var normalB = TriangleNormal(v4, v2, v3);

        // written this way so nan normals from degenerate triangles also delimit
        if (!(Vector3.Dot(normalA, normalB) >= angleFaceCos))
            return false;

        // a flipped face is out of the question
        if (IsQuadFlip(v1, v2, v3, v4))
            return false;

        var e0 = Vector3.Normalize(v1 - v2);
        var e1 = Vector3.Normalize(v2 - v3);
        var e2 = Vector3.Normalize(v3 - v4);
        var e3 = Vector3.Normalize(v4 - v1);

        // every corner of the quad must stay within the shape angle limit of 90 degrees, inverted comparisons so nan also fails
        if (!(MathF.Abs(AngleNormalized(e0, e1) - HalfPi) <= angleShape) ||
            !(MathF.Abs(AngleNormalized(e1, e2) - HalfPi) <= angleShape) ||
            !(MathF.Abs(AngleNormalized(e2, e3) - HalfPi) <= angleShape) ||
            !(MathF.Abs(AngleNormalized(e3, e0) - HalfPi) <= angleShape))
        {
            return false;
        }

        return true;
    }

    // gives a weight to a pair of triangles that join an edge to decide how good a join they would make, lower is better
    private static float QuadCalcError(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
    {
        const float HalfPi = MathF.PI / 2f;

        var error = 0f;

        // normal difference, planarity of the quad measured across both diagonals
        {
            var angleA = AngleNormalized(TriangleNormal(v1, v2, v3), TriangleNormal(v1, v3, v4));
            var angleB = AngleNormalized(TriangleNormal(v2, v3, v4), TriangleNormal(v4, v1, v2));

            error += (angleA + angleB) / (MathF.PI * 2f);
        }

        // colinearity, how far the corners deviate from 90 degrees
        {
            var e0 = Vector3.Normalize(v1 - v2);
            var e1 = Vector3.Normalize(v2 - v3);
            var e2 = Vector3.Normalize(v3 - v4);
            var e3 = Vector3.Normalize(v4 - v1);

            error += (MathF.Abs(AngleNormalized(e0, e1) - HalfPi) +
                      MathF.Abs(AngleNormalized(e1, e2) - HalfPi) +
                      MathF.Abs(AngleNormalized(e2, e3) - HalfPi) +
                      MathF.Abs(AngleNormalized(e3, e0) - HalfPi)) / (MathF.PI * 2f);
        }

        // concavity, area imbalance between the two diagonal splits
        {
            var areaA = TriangleArea(v1, v2, v3) + TriangleArea(v1, v3, v4);
            var areaB = TriangleArea(v2, v3, v4) + TriangleArea(v4, v1, v2);

            var areaMin = MathF.Min(areaA, areaB);
            var areaMax = MathF.Max(areaA, areaB);

            error += areaMax > 0f ? 1f - (areaMin / areaMax) : 1f;
        }

        return error;
    }

    // true when the quad is twisted or concave enough that the winding of its corners disagrees
    private static bool IsQuadFlip(Vector3 v1, Vector3 v2, Vector3 v3, Vector3 v4)
    {
        var d12 = v2 - v1;
        var d23 = v3 - v2;
        var d34 = v4 - v3;
        var d41 = v1 - v4;

        if (Vector3.Dot(Vector3.Cross(d12, d23), Vector3.Cross(d34, d41)) < 0f)
            return true;

        if (Vector3.Dot(Vector3.Cross(d23, d34), Vector3.Cross(d41, d12)) < 0f)
            return true;

        return false;
    }

    private static Vector3 TriangleNormal(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Normalize(Vector3.Cross(b - a, c - a));
    }

    private static float TriangleArea(Vector3 a, Vector3 b, Vector3 c)
    {
        return Vector3.Cross(b - a, c - a).Length() * 0.5f;
    }

    private static float AngleNormalized(Vector3 a, Vector3 b)
    {
        return MathF.Acos(Math.Clamp(Vector3.Dot(a, b), -1f, 1f));
    }
}

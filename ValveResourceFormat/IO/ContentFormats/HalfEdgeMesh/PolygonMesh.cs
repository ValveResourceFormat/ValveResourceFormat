using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

/// <summary>
/// An editable polygon mesh: a <see cref="HalfEdgeMesh"/> topology carrying the data of a Hammer mesh, positions per
/// vertex, corner data per half edge and a material per face, with the editing operations that keep that data
/// right. Taken from <see href="https://github.com/Facepunch/sbox-public/blob/master/engine/Sandbox.Engine/Scene/Components/Mesh/PolygonMesh.cs">Sbox</see>
/// </summary>
public sealed class PolygonMesh
{
    /// <summary>The half edge topology.</summary>
    public HalfEdgeMesh Topology { get; } = new();

    /// <summary>Position per vertex.</summary>
    public VertexData<Vector3> Positions { get; }
    /// <summary>Texture coordinates per corner.</summary>
    public HalfEdgeData<Vector2> TextureCoords { get; }
    /// <summary>Second texture coordinates per corner.</summary>
    public HalfEdgeData<Vector2> TextureCoords1 { get; }
    /// <summary>Normal per corner.</summary>
    public HalfEdgeData<Vector3> Normals { get; }
    /// <summary>Tangent per corner.</summary>
    public HalfEdgeData<Vector4> Tangents { get; }
    /// <summary>Vertex paint blend parameters per corner.</summary>
    public HalfEdgeData<Vector4> VertexPaintBlendParams { get; }
    /// <summary>Vertex paint tint per corner.</summary>
    public HalfEdgeData<Vector4> VertexPaintTintColor { get; }
    /// <summary>Index into <see cref="Materials"/> per face, -1 for no material.</summary>
    public FaceData<int> MaterialIndex { get; }

    private readonly List<string> materials = [];
    private readonly Dictionary<string, int> materialIds = [];

    /// <summary>Materials used by the faces, in <see cref="MaterialIndex"/> order.</summary>
    public IReadOnlyList<string> Materials => materials;

    /// <summary>The condition under which <see cref="DissolveEdges"/> removes the vertices left at the ends of dissolved edges.</summary>
    public enum DissolveRemoveVertexCondition
    {
        /// <summary>Never remove vertices.</summary>
        None,
        /// <summary>Remove vertices with only 2 edges attached that are colinear.</summary>
        Colinear,
        /// <summary>Remove vertices with only 2 edges attached that are interior edges (not open) or are colinear.</summary>
        InteriorOrColinear,
        /// <summary>Remove all vertices with only 2 edges attached.</summary>
        All,
    }

    /// <summary>
    /// Initializes a new instance of the <see cref="PolygonMesh"/> class.
    /// </summary>
    public PolygonMesh()
    {
        Positions = Topology.CreateVertexData<Vector3>(nameof(Positions));
        TextureCoords = Topology.CreateHalfEdgeData<Vector2>(nameof(TextureCoords));
        TextureCoords1 = Topology.CreateHalfEdgeData<Vector2>(nameof(TextureCoords1));
        Normals = Topology.CreateHalfEdgeData<Vector3>(nameof(Normals));
        Tangents = Topology.CreateHalfEdgeData<Vector4>(nameof(Tangents));
        VertexPaintBlendParams = Topology.CreateHalfEdgeData<Vector4>(nameof(VertexPaintBlendParams));
        VertexPaintTintColor = Topology.CreateHalfEdgeData<Vector4>(nameof(VertexPaintTintColor));
        MaterialIndex = Topology.CreateFaceData<int>(nameof(MaterialIndex));

        Topology.OnCopyFaceVertexData = (dst, src) =>
        {
            TextureCoords[dst] = TextureCoords[src];
            TextureCoords1[dst] = TextureCoords1[src];
            Normals[dst] = Normals[src];
            Tangents[dst] = Tangents[src];
            VertexPaintBlendParams[dst] = VertexPaintBlendParams[src];
            VertexPaintTintColor[dst] = VertexPaintTintColor[src];
        };

        Topology.OnClearFaceVertexData = (hEdge) =>
        {
            TextureCoords[hEdge] = default;
            TextureCoords1[hEdge] = default;
            Normals[hEdge] = default;
            Tangents[hEdge] = default;
            VertexPaintBlendParams[hEdge] = default;
            VertexPaintTintColor[hEdge] = default;
        };
    }

    /// <summary>Vertices of the mesh.</summary>
    public IEnumerable<VertexHandle> VertexHandles => Topology.VertexHandles;
    /// <summary>Faces of the mesh.</summary>
    public IEnumerable<FaceHandle> FaceHandles => Topology.FaceHandles;
    /// <summary>Half edges of the mesh.</summary>
    public IEnumerable<HalfEdgeHandle> HalfEdgeHandles => Topology.HalfEdgeHandles;

    /// <summary>
    /// Returns the index of a material, adding it when it is new.
    /// </summary>
    public int AddMaterial(string? material)
    {
        if (material is null)
        {
            return -1;
        }

        if (materialIds.TryGetValue(material, out var id))
        {
            return id;
        }

        id = materials.Count;
        materials.Add(material);
        materialIds[material] = id;

        return id;
    }

    /// <summary>
    /// Gets the material of a face, null when it has none.
    /// </summary>
    public string? GetFaceMaterial(FaceHandle hFace)
    {
        var index = MaterialIndex[hFace];
        return index >= 0 && index < materials.Count ? materials[index] : null;
    }

    /// <summary>
    /// Sets the material of a face.
    /// </summary>
    public void SetFaceMaterial(FaceHandle hFace, string? material)
    {
        MaterialIndex[hFace] = AddMaterial(material);
    }

    /// <summary>
    /// Get the position of a vertex
    /// </summary>
    public Vector3 GetVertexPosition(VertexHandle hVertex)
    {
        return Positions[hVertex];
    }

    /// <summary>
    /// Set the position of a vertex
    /// </summary>
    public void SetVertexPosition(VertexHandle hVertex, Vector3 position)
    {
        if (!hVertex.IsValid)
            return;

        Positions[hVertex] = position;
    }

    /// <summary>
    /// Gets the vertices of a face, in loop order.
    /// </summary>
    public static VertexHandle[] GetFaceVertices(FaceHandle hFace)
    {
        HalfEdgeMesh.GetVerticesConnectedToFace(hFace, out var vertices);
        return vertices ?? [];
    }

    /// <summary>
    /// Get start and end points of an edge
    /// </summary>
    public void GetEdgeVertexPositions(HalfEdgeHandle hEdge, out Vector3 pOutVertexA, out Vector3 pOutVertexB)
    {
        HalfEdgeMesh.GetVerticesConnectedToHalfEdge(hEdge, out var hVertexA, out var hVertexB);
        pOutVertexA = GetVertexPosition(hVertexA);
        pOutVertexB = GetVertexPosition(hVertexB);
    }

    /// <summary>
    /// Finds the full edge connecting two vertices.
    /// </summary>
    public HalfEdgeHandle FindEdgeConnectingVertices(VertexHandle hVertexA, VertexHandle hVertexB)
    {
        return Topology.FindFullEdgeConnectingVertices(hVertexA, hVertexB);
    }

    /// <summary>
    /// Copies another mesh into this one, reporting the handle each source component landed on.
    /// </summary>
    public void MergeMesh(PolygonMesh sourceMesh,
        out Dictionary<VertexHandle, VertexHandle> newVertices,
        out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
        out Dictionary<FaceHandle, FaceHandle> newFaces)
    {
        Topology.AppendComponentsFromMesh(sourceMesh.Topology, out newVertices, out newHalfEdges, out newFaces);
        CopyMergedData(sourceMesh, newVertices, newHalfEdges, newFaces);
    }

    /// <summary>
    /// Copies a set of faces of another mesh into this one, with their edges and vertices, reporting the handle
    /// each source component landed on. The faces should form whole islands connected through edges.
    /// </summary>
    public void MergeMesh(PolygonMesh sourceMesh, IReadOnlyCollection<FaceHandle> faces,
        out Dictionary<VertexHandle, VertexHandle> newVertices,
        out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
        out Dictionary<FaceHandle, FaceHandle> newFaces)
    {
        Topology.AppendComponentsFromMesh(sourceMesh.Topology, faces, out newVertices, out newHalfEdges, out newFaces);
        CopyMergedData(sourceMesh, newVertices, newHalfEdges, newFaces);
    }

    private void CopyMergedData(PolygonMesh sourceMesh,
        Dictionary<VertexHandle, VertexHandle> newVertices,
        Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
        Dictionary<FaceHandle, FaceHandle> newFaces)
    {
        foreach (var (hVertex, hNewVertex) in newVertices)
        {
            Positions[hNewVertex] = sourceMesh.Positions[hVertex];
        }

        foreach (var (hEdge, hNewEdge) in newHalfEdges)
        {
            TextureCoords[hNewEdge] = sourceMesh.TextureCoords[hEdge];
            TextureCoords1[hNewEdge] = sourceMesh.TextureCoords1[hEdge];
            Normals[hNewEdge] = sourceMesh.Normals[hEdge];
            Tangents[hNewEdge] = sourceMesh.Tangents[hEdge];
            VertexPaintBlendParams[hNewEdge] = sourceMesh.VertexPaintBlendParams[hEdge];
            VertexPaintTintColor[hNewEdge] = sourceMesh.VertexPaintTintColor[hEdge];
        }

        foreach (var (hFace, hNewFace) in newFaces)
        {
            SetFaceMaterial(hNewFace, sourceMesh.GetFaceMaterial(hFace));
        }
    }

    /// <summary>
    /// Computes the normal of a face.
    /// </summary>
    public void ComputeFaceNormal(FaceHandle hFace, out Vector3 pOutNormal)
    {
        PlaneEquation(hFace, out pOutNormal, out _);
    }

    private static void AccumulateNewellPair(ref Vector3 vNormal, in Vector3 pU, in Vector3 pV)
    {
        vNormal.X += (pU.Y - pV.Y) * (pU.Z + pV.Z);
        vNormal.Y += (pU.Z - pV.Z) * (pU.X + pV.X);
        vNormal.Z += (pU.X - pV.X) * (pU.Y + pV.Y);
    }

    private static void FinaliseNewellNormal(in Vector3 vNormal, in Vector3 refpt, int count, out Vector3 pOutNormal, out float pOutPlaneDistance)
    {
        var len = vNormal.Length() + 1.192092896e-07F;
        pOutNormal = vNormal * (1.0f / len);
        len *= count;
        pOutPlaneDistance = -Vector3.Dot(refpt, vNormal) / len;
    }

    // Computes the Newell normal directly from face topology without allocating.
    // The positions are taken relative to the first vertex (deviation from S&box): Newell's sums of coordinate
    // products lose precision on map sized coordinates, enough for two coplanar triangles to get normals that
    // differ in the fourth decimal, relative to the face they are exact.
    private void PlaneEquation(FaceHandle hFace, out Vector3 pOutNormal, out float pOutPlaneDistance)
    {
        var vNormal = Vector3.Zero;
        var refpt = Vector3.Zero;
        var count = 0;
        var origin = Vector3.Zero;
        var first = Vector3.Zero;
        var prev = Vector3.Zero;

        var hEdge = hFace.Edge;
        do
        {
            var absolute = Positions[hEdge.Vertex];

            if (count == 0)
            {
                origin = absolute;
            }

            var pos = absolute - origin;

            if (count == 0)
            {
                first = prev = pos;
            }
            else
            {
                AccumulateNewellPair(ref vNormal, prev, pos);
            }

            refpt += absolute;
            prev = pos;
            count++;

            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);

        if (count > 0)
            AccumulateNewellPair(ref vNormal, prev, first);

        FinaliseNewellNormal(vNormal, refpt, count, out pOutNormal, out pOutPlaneDistance);
    }

    /// <summary>
    /// Whether two edges run along the same line within an angle.
    /// </summary>
    public bool AreEdgesCoLinear(HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, float flAngleToleranceInDegrees)
    {
        var flTolerance = MathF.Cos(MathF.Min(flAngleToleranceInDegrees, 180.0f) * (MathF.PI / 180f));

        if ((hEdgeA == HalfEdgeHandle.Invalid) || (hEdgeB == HalfEdgeHandle.Invalid))
            return false;

        GetEdgeVertexPositions(hEdgeA, out var vPositionA1, out var vPositionA2);
        GetEdgeVertexPositions(hEdgeB, out var vPositionB1, out var vPositionB2);

        var vEdgeA = Vector3.Normalize(vPositionA2 - vPositionA1);
        var vEdgeB = Vector3.Normalize(vPositionB2 - vPositionB1);

        var flCosAngle = MathF.Abs(Vector3.Dot(vEdgeA, vEdgeB));
        return flCosAngle > flTolerance;
    }

    /// <summary>
    /// Combines a set of faces into one by dissolving the edges they share, removing the vertices left on straight runs.
    /// </summary>
    public void CombineFaces(IReadOnlyList<FaceHandle> faces)
    {
        FindEdgesConnectedToFaces(faces, out var connectedEdges, out var edgeFaceCounts);

        connectedEdges = connectedEdges
            .Where((edge, i) => edgeFaceCounts[i] >= 2)
            .ToArray();

        DissolveEdges(connectedEdges, true, DissolveRemoveVertexCondition.Colinear);
    }

    /// <summary>
    /// Merges the two faces of an edge by removing it.
    /// </summary>
    public void DissolveEdge(HalfEdgeHandle edge)
    {
        Topology.DissolveEdge(edge, out _);
    }

    /// <summary>
    /// Merges the faces on either side of each edge by removing the edges, optionally only where the faces are
    /// coplanar, and removes the end vertices that are left with only two edges under the given condition.
    /// </summary>
    public void DissolveEdges(IReadOnlyList<HalfEdgeHandle> edges, bool bFaceMustBePlanar, DissolveRemoveVertexCondition removeCondition)
    {
        const float flColinearTolerance = 5.0f; // Edges may be at an angle of up to this many degrees and still be considered co-linear
        const float flPlanarTolerance = 0.01f;

        var nNumEdges = edges.Count;

        var verticesToRemove = new List<VertexHandle>(nNumEdges);
        var combinedFaces = new List<FaceHandle>(nNumEdges);

        for (var iEdge = 0; iEdge < nNumEdges; ++iEdge)
        {
            var hEdge = edges[iEdge];

            // Get the two faces connected to the edge, if the faces are not in the same plane then any two
            // edge vertices will left behind will be removed to prevent the creation of a non-planar polygon.
            HalfEdgeMesh.GetFacesConnectedToFullEdge(hEdge, out var hFaceA, out var hFaceB);
            if ((hFaceA == FaceHandle.Invalid) || (hFaceB == FaceHandle.Invalid))
                continue;

            ComputeFaceNormal(hFaceA, out var normalA);
            ComputeFaceNormal(hFaceB, out var normalB);

            var flFaceAngle = Vector3.Dot(normalA, normalB);
            if (bFaceMustBePlanar && (flFaceAngle < (1.0f - flPlanarTolerance)))
                continue;

            // Get the vertices connected to the edge
            HalfEdgeMesh.GetVerticesConnectedToFullEdge(hEdge, out var hVertexA, out var hVertexB);

            // Dissolve the edge
            if (!Topology.DissolveEdge(hEdge, out var hFace))
                continue;

            // Determine if the vertices at the ends of the edge should be removed. A vertex should be
            // removed if after the edge is dissolved it has only 2 edges connected to it and it passes
            // the specified removal criteria. Note the vertices are not removed here, but are placed
            // in a list of vertices to be removed, this is because removing the vertices might result
            // in removing one of the edges in the list of edges to be dissolved.

            if (ShouldDissolveRemoveVertex(hVertexA, removeCondition, flColinearTolerance))
            {
                verticesToRemove.Add(hVertexA);
            }

            if (ShouldDissolveRemoveVertex(hVertexB, removeCondition, flColinearTolerance))
            {
                verticesToRemove.Add(hVertexB);
            }

            combinedFaces.Add(hFace);
        }

        // Now that all of the edges have been dissolved remove all of the vertices that were
        // determined should be removed while dissolving the edges.
        var nNumVerticesToRemove = verticesToRemove.Count;
        for (var iVertex = 0; iVertex < nNumVerticesToRemove; ++iVertex)
        {
            Topology.RemoveVertex(verticesToRemove[iVertex], true);
        }

        // Make sure there are no remaining co-linear edges in the face, this may happen if removing
        // the edge cause there to be a loose edge in the face that is subsequently removed.
        if (removeCondition != DissolveRemoveVertexCondition.None)
        {
            var nNumFaces = combinedFaces.Count;
            for (var iFace = 0; iFace < nNumFaces; ++iFace)
            {
                RemoveVerticesFromColinearEdgesInFace(combinedFaces[iFace], flColinearTolerance);
            }
        }
    }

    private bool ShouldDissolveRemoveVertex(VertexHandle hVertex, DissolveRemoveVertexCondition removeCondition, float flColinearTolerance)
    {
        if (removeCondition == DissolveRemoveVertexCondition.None)
            return false;

        if (!hVertex.IsValid || HalfEdgeMesh.ComputeNumEdgesConnectedToVertex(hVertex) != 2)
            return false;

        Topology.GetFullEdgesConnectedToVertex(hVertex, out var connectedEdges);
        var bInterior = !HalfEdgeMesh.IsFullEdgeOpen(connectedEdges[0]);
        var bColinear = AreEdgesCoLinear(connectedEdges[0], connectedEdges[1], flColinearTolerance);

        return removeCondition switch
        {
            DissolveRemoveVertexCondition.InteriorOrColinear => bInterior || bColinear,
            DissolveRemoveVertexCondition.Colinear => bColinear,
            DissolveRemoveVertexCondition.All => true,
            _ => false,
        };
    }

    private void RemoveVerticesFromColinearEdgesInFace(FaceHandle hFace, float flColinearAngleTolerance)
    {
        if (!hFace.IsValid)
            return;

        // Get all of the vertices in the face
        HalfEdgeMesh.GetVerticesConnectedToFace(hFace, out var verticesInFace);
        if (verticesInFace is null || verticesInFace.Length == 0)
            return;

        // Iterate over all of the vertices connected to the face, find the ones which are only connected
        // to two edges and determine if those two edges are co-linear, if so remove the vertex.
        var nNumVertices = verticesInFace.Length;
        for (var iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            RemoveColinearVertex(verticesInFace[iVertex], flColinearAngleTolerance);
        }
    }

    /// <summary>
    /// Removes a vertex connected to exactly two edges that continue each other, merging them into one edge.
    /// </summary>
    public bool RemoveColinearVertex(VertexHandle hVertex, float flColinearAngleTolerance = 5.0f)
    {
        if (!hVertex.IsValid)
            return false;

        Topology.GetFullEdgesConnectedToVertex(hVertex, out var edgesConnectedToVertex);

        if (edgesConnectedToVertex is not null && edgesConnectedToVertex.Count == 2)
        {
            if (AreEdgesCoLinear(edgesConnectedToVertex[0], edgesConnectedToVertex[1], flColinearAngleTolerance))
            {
                // Remove the vertex, combining the two edges into a single edge
                return Topology.RemoveVertex(hVertex, true);
            }
        }

        return false;
    }

    // the full edges used by a set of faces and how many of those faces each one borders
    private static void FindEdgesConnectedToFaces(IReadOnlyList<FaceHandle> faces, out HalfEdgeHandle[] edges, out int[] edgeFaceCounts)
    {
        var counts = new Dictionary<HalfEdgeHandle, int>();
        var order = new List<HalfEdgeHandle>();

        foreach (var hFace in faces)
        {
            var hEdge = hFace.Edge;
            do
            {
                // one half edge of each pair stands for the full edge
                var hFull = hEdge.Index < hEdge.OppositeEdge.Index ? hEdge : hEdge.OppositeEdge;

                if (!counts.TryGetValue(hFull, out var count))
                {
                    order.Add(hFull);
                }

                counts[hFull] = count + 1;
                hEdge = hEdge.NextEdge;
            }
            while (hEdge != hFace.Edge);
        }

        edges = [.. order];
        edgeFaceCounts = order.Select(e => counts[e]).ToArray();
    }

    /// <summary>
    /// Merges two edges into one, merging their end vertices pairwise.
    /// </summary>
    public bool MergeEdges(HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out HalfEdgeHandle hOutNewEdge)
    {
        hOutNewEdge = HalfEdgeHandle.Invalid;

        if (!HalfEdgeMesh.GetEdgeMergeVertexPairs(hEdgeA, hEdgeB, out var hVertexPairA1, out var hVertexPairA2, out var hVertexPairB1, out var hVertexPairB2))
            return false;

        // Check to see of the edges share a single vertex,
        // if so just merge the other vertex instead of the edge.
        var hSharedVertex = VertexHandle.Invalid;
        var hMergeVertexA = VertexHandle.Invalid;
        var hMergeVertexB = VertexHandle.Invalid;

        if (hVertexPairA1 == hVertexPairA2)
        {
            hSharedVertex = hVertexPairA1;
            hMergeVertexA = hVertexPairB1;
            hMergeVertexB = hVertexPairB2;
        }
        else if (hVertexPairB1 == hVertexPairB2)
        {
            hSharedVertex = hVertexPairB1;
            hMergeVertexA = hVertexPairA1;
            hMergeVertexB = hVertexPairA2;
        }

        if (hSharedVertex != VertexHandle.Invalid)
        {
            if (!MergeVertices(hMergeVertexA, hMergeVertexB, 0.5f, out var hNewVertex))
                return false;

            hOutNewEdge = FindEdgeConnectingVertices(hNewVertex, hSharedVertex);

            return true;
        }

        // No vertices are shared between the edges, merge them.
        var a = Vector3.Lerp(GetVertexPosition(hVertexPairA1), GetVertexPosition(hVertexPairA2), 0.5f);
        var b = Vector3.Lerp(GetVertexPosition(hVertexPairB1), GetVertexPosition(hVertexPairB2), 0.5f);

        if (Topology.MergeEdges(hEdgeA, hEdgeB, out var hNewVertexA, out var hNewVertexB))
        {
            SetVertexPosition(hNewVertexA, a);
            SetVertexPosition(hNewVertexB, b);

            hOutNewEdge = FindEdgeConnectingVertices(hNewVertexA, hNewVertexB);

            return true;
        }

        return false;
    }

    /// <summary>
    /// Splits the edge between two vertices by inserting a new vertex at the given parameter along it, interpolating
    /// the vertex and corner data.
    /// </summary>
    public bool AddVertexToEdge(VertexHandle hVertexA, VertexHandle hVertexB, float flParam, out VertexHandle pOutNewVertex)
    {
        pOutNewVertex = VertexHandle.Invalid;

        var hEdge = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hVertexA, hVertexB);
        if (!hEdge.IsValid)
            return false;

        var hPrevEdge = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hEdge);
        var hOpposite = HalfEdgeMesh.GetOppositeHalfEdge(hEdge);
        var hOppositePrev = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hOpposite);

        // Add the new vertex to the edge, this will result in the edge being split into two edges.
        if (!Topology.AddVertexToEdge(hEdge, out var hNewVertex))
            return false;

        // Interpolate the values of the vertices to compute the value of the new vertex
        InterpolateVertexData(hNewVertex, hVertexA, hVertexB, flParam);

        // Interpolate the value of the face vertices connected to each face to compute the value of the
        // new face vertex on each side of the edge.
        var hEdgeAToNew = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hVertexA, hNewVertex);
        var hEdgeNewToB = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hNewVertex, hVertexB);
        InterpolateFaceVertexData(hEdgeAToNew, hPrevEdge, hEdgeNewToB, flParam);

        var hEdgeBToNew = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hVertexB, hNewVertex);
        var hEdgeNewToA = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hNewVertex, hVertexA);
        InterpolateFaceVertexData(hEdgeBToNew, hEdgeNewToA, hOppositePrev, flParam);

        pOutNewVertex = hNewVertex;

        return true;
    }

    private void InterpolateVertexData(VertexHandle hDstVertex, VertexHandle hVertexA, VertexHandle hVertexB, float param)
    {
        if (!hDstVertex.IsValid || !hVertexA.IsValid || !hVertexB.IsValid)
            return;

        SetVertexPosition(hDstVertex, Vector3.Lerp(GetVertexPosition(hVertexA), GetVertexPosition(hVertexB), param));
    }

    private void InterpolateFaceVertexData(HalfEdgeHandle hDstFaceVertex, HalfEdgeHandle hFaceVertexA, HalfEdgeHandle hFaceVertexB, float param)
    {
        if (!hDstFaceVertex.IsValid || !hFaceVertexA.IsValid || !hFaceVertexB.IsValid)
            return;

        TextureCoords[hDstFaceVertex] = Vector2.Lerp(TextureCoords[hFaceVertexA], TextureCoords[hFaceVertexB], param);
        TextureCoords1[hDstFaceVertex] = Vector2.Lerp(TextureCoords1[hFaceVertexA], TextureCoords1[hFaceVertexB], param);
        Normals[hDstFaceVertex] = Vector3.Normalize(Vector3.Lerp(Normals[hFaceVertexA], Normals[hFaceVertexB], param));
        Tangents[hDstFaceVertex] = Vector4.Lerp(Tangents[hFaceVertexA], Tangents[hFaceVertexB], param);
        VertexPaintBlendParams[hDstFaceVertex] = Vector4.Lerp(VertexPaintBlendParams[hFaceVertexA], VertexPaintBlendParams[hFaceVertexB], param);
        VertexPaintTintColor[hDstFaceVertex] = Vector4.Lerp(VertexPaintTintColor[hFaceVertexA], VertexPaintTintColor[hFaceVertexB], param);
    }

    /// <summary>
    /// Joins pairs of coplanar triangles into quads where they use the same material and agree on their corner
    /// data along the shared edge, so no texture seam, hard edge or vertex paint break gets stretched over a quad.
    /// </summary>
    /// <returns>Number of triangle pairs joined.</returns>
    public int Untriangulate(float maxFaceAngleDegrees = 40f)
    {
        return Topology.UntriangulateMesh(Positions, CanUntriangulateFaces, maxFaceAngleDegrees);
    }

    // two triangles only merge into a quad when they use the same material and carry the same corner data at the
    // two vertices of the edge they share, the quad keeps one corner per vertex
    private bool CanUntriangulateFaces(FaceHandle hFaceA, FaceHandle hFaceB)
    {
        if (MaterialIndex[hFaceA] != MaterialIndex[hFaceB])
        {
            return false;
        }

        var hEdge = hFaceA.Edge;
        do
        {
            if (hEdge.OppositeEdge.Face == hFaceB)
            {
                break;
            }

            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFaceA.Edge);

        var hOpposite = hEdge.OppositeEdge;
        if (hOpposite.Face != hFaceB)
        {
            return false;
        }

        // a half edge carries the corner at its end vertex: at the shared edge's end the corners are hEdge on
        // face A and the edge before the opposite on face B, at its start the edge before hEdge and the opposite
        return CornerDataMatches(hEdge, HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hOpposite))
            && CornerDataMatches(HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hEdge), hOpposite);
    }

    private bool CornerDataMatches(HalfEdgeHandle hCornerA, HalfEdgeHandle hCornerB)
    {
        const float TexCoordEpsilon = 1f / 1024f;
        const float NormalEpsilon = 0.02f;
        const float PaintEpsilon = 1f / 255f;

        return Vector2.Distance(TextureCoords[hCornerA], TextureCoords[hCornerB]) <= TexCoordEpsilon
            && Vector3.Distance(Normals[hCornerA], Normals[hCornerB]) <= NormalEpsilon
            && Vector4.Distance(VertexPaintBlendParams[hCornerA], VertexPaintBlendParams[hCornerB]) <= PaintEpsilon
            && Vector4.Distance(VertexPaintTintColor[hCornerA], VertexPaintTintColor[hCornerB]) <= PaintEpsilon;
    }

    /// <summary>
    /// Groups the faces into islands connected through shared edges. Faces that only touch at a vertex stay apart,
    /// so a bowtie vertex doesn't glue two separate objects together. Loose faces have no shared edges at all (their
    /// vertices were duplicated), they are grouped through coinciding vertex positions instead so the pieces of one
    /// object stay together without collecting the whole mesh's rejects in one lump.
    /// </summary>
    /// <param name="isLoose">Tells the faces to group by position instead, null for none.</param>
    public List<List<FaceHandle>> FindIslands(Func<FaceHandle, bool>? isLoose = null)
    {
        var parent = new int[Topology.FaceCount];
        for (var i = 0; i < parent.Length; i++)
        {
            parent[i] = i;
        }

        var extractedFaceAtPosition = new Dictionary<(int X, int Y, int Z), int>();

        int Find(int i)
        {
            while (parent[i] != i)
            {
                parent[i] = parent[parent[i]];
                i = parent[i];
            }

            return i;
        }

        void Union(int a, int b)
        {
            a = Find(a);
            b = Find(b);

            if (a != b)
            {
                parent[a] = b;
            }
        }

        foreach (var hFace in Topology.FaceHandles)
        {
            var extracted = isLoose?.Invoke(hFace) == true;
            var hEdge = hFace.Edge;
            do
            {
                var hNeighbour = hEdge.OppositeEdge.Face;
                if (hNeighbour.IsValid)
                {
                    Union(hFace.Index, hNeighbour.Index);
                }

                if (extracted)
                {
                    var position = Positions[hEdge.Vertex];
                    var cell = ((int)MathF.Floor(position.X * 64f), (int)MathF.Floor(position.Y * 64f), (int)MathF.Floor(position.Z * 64f));

                    if (extractedFaceAtPosition.TryGetValue(cell, out var otherFace))
                    {
                        Union(hFace.Index, otherFace);
                    }
                    else
                    {
                        extractedFaceAtPosition.Add(cell, hFace.Index);
                    }
                }

                hEdge = hEdge.NextEdge;
            }
            while (hEdge != hFace.Edge);
        }

        var islandByRoot = new Dictionary<int, List<FaceHandle>>();
        var islands = new List<List<FaceHandle>>();

        foreach (var hFace in Topology.FaceHandles)
        {
            var root = Find(hFace.Index);

            if (!islandByRoot.TryGetValue(root, out var island))
            {
                island = [];
                islandByRoot.Add(root, island);
                islands.Add(island);
            }

            island.Add(hFace);
        }

        return islands;
    }

    /// <summary>
    /// Merges every pair of open edges that lie on top of each other, running in opposite directions, the way two
    /// sheets meet along a seam. Merging the seams edge by edge joins the sheets without creating bowtie vertices,
    /// which merging vertex by vertex does when two faces that only touch at a point get merged first. Use before
    /// <see cref="MergeVerticesWithinDistance(float)"/>.
    /// </summary>
    /// <param name="maxDistance">Largest distance between the edge end points that still counts as coinciding.</param>
    /// <returns>Number of edges merged.</returns>
    public int MergeCoincidentOpenEdges(float maxDistance)
    {
        var maxDistanceSquared = maxDistance * maxDistance;
        var invCellSize = 1f / maxDistance;

        // vertices by position cell, so the copies of a position can be looked up; handles go stale when
        // vertices are merged, that is checked on lookup, new vertices are added as they appear
        var cells = new Dictionary<(int X, int Y, int Z), List<VertexHandle>>();

        (int X, int Y, int Z) CellOf(Vector3 p) => ((int)MathF.Floor(p.X * invCellSize), (int)MathF.Floor(p.Y * invCellSize), (int)MathF.Floor(p.Z * invCellSize));

        void Register(VertexHandle hVertex)
        {
            var cell = CellOf(Positions[hVertex]);
            if (!cells.TryGetValue(cell, out var list))
            {
                list = [];
                cells.Add(cell, list);
            }

            list.Add(hVertex);
        }

        IEnumerable<VertexHandle> VerticesNear(Vector3 p)
        {
            var cell = CellOf(p);
            for (var dx = -1; dx <= 1; dx++)
            {
                for (var dy = -1; dy <= 1; dy++)
                {
                    for (var dz = -1; dz <= 1; dz++)
                    {
                        if (!cells.TryGetValue((cell.X + dx, cell.Y + dy, cell.Z + dz), out var list))
                        {
                            continue;
                        }

                        foreach (var hVertex in list)
                        {
                            if (hVertex.IsValid && Vector3.DistanceSquared(Positions[hVertex], p) <= maxDistanceSquared)
                            {
                                yield return hVertex;
                            }
                        }
                    }
                }
            }
        }

        foreach (var hVertex in Topology.VertexHandles)
        {
            Register(hVertex);
        }

        var queue = new Queue<HalfEdgeHandle>();
        foreach (var hEdge in Topology.HalfEdgeHandles)
        {
            if (hEdge.Face == FaceHandle.Invalid)
            {
                queue.Enqueue(hEdge);
            }
        }

        var merged = 0;

        while (queue.TryDequeue(out var hEdge))
        {
            // the edge may have been merged away, or closed up, since it was queued
            if (!hEdge.IsValid || hEdge.Face != FaceHandle.Invalid)
            {
                continue;
            }

            var hStart = hEdge.OppositeEdge.Vertex;
            var hEnd = hEdge.Vertex;
            var startPosition = Positions[hStart];
            var endPosition = Positions[hEnd];

            // look for an open edge of another fan running from a copy of the end to a copy of the start
            var hPartner = HalfEdgeHandle.Invalid;
            foreach (var hOtherStart in VerticesNear(startPosition))
            {
                if (hOtherStart == hStart)
                {
                    continue;
                }

                if (!HalfEdgeMesh.GetIncomingHalfEdgesConnectedToVertex(hOtherStart, out var incoming))
                {
                    continue;
                }

                foreach (var hIncoming in incoming)
                {
                    if (hIncoming.Face == FaceHandle.Invalid
                        && hIncoming.OppositeEdge.Face != FaceHandle.Invalid
                        && Vector3.DistanceSquared(Positions[hIncoming.OppositeEdge.Vertex], endPosition) <= maxDistanceSquared)
                    {
                        hPartner = hIncoming;
                        break;
                    }
                }

                if (hPartner.IsValid)
                {
                    break;
                }
            }

            if (!hPartner.IsValid)
            {
                continue;
            }

            // MergeEdges merges the two end point pairs one after the other. The first pair can be merged
            // while the second is refused, in which case it reports failure but has already made the first
            // merged vertex, so whatever vertices come back get their position, no matter the result.
            // The topology doesn't know positions, the merged vertices sit where the edge was
            var success = Topology.MergeEdges(hEdge, hPartner, out var hNewVertexA, out var hNewVertexB);

            foreach (var (hNewVertex, position) in new[] { (hNewVertexA, endPosition), (hNewVertexB, startPosition) })
            {
                if (!hNewVertex.IsValid)
                {
                    continue;
                }

                Positions[hNewVertex] = position;
                Register(hNewVertex);
            }

            if (!success)
            {
                continue;
            }

            merged++;

            foreach (var hNewVertex in new[] { hNewVertexA, hNewVertexB })
            {
                if (!hNewVertex.IsValid)
                {
                    continue;
                }

                // the merged vertices have new open edges that may have partners of their own
                if (HalfEdgeMesh.GetOutgoingHalfEdgesConnectedToVertex(hNewVertex, out var outgoing))
                {
                    foreach (var hOutgoing in outgoing)
                    {
                        if (hOutgoing.Face == FaceHandle.Invalid)
                        {
                            queue.Enqueue(hOutgoing);
                        }

                        if (hOutgoing.OppositeEdge.Face == FaceHandle.Invalid)
                        {
                            queue.Enqueue(hOutgoing.OppositeEdge);
                        }
                    }
                }
            }
        }

        return merged;
    }

    /// <summary>
    /// Merges every vertex of the mesh that lies within <paramref name="maxDistance"/> of another one, the
    /// Hammer "merge vertices by distance" operation. Use after all faces were added: the input vertex indices
    /// the builder handed out no longer apply afterwards.
    /// </summary>
    /// <param name="maxDistance">Largest distance between two vertices that still get merged.</param>
    /// <returns>Number of vertices merged away.</returns>
    public int MergeVerticesWithinDistance(float maxDistance)
    {
        var total = 0;
        var vertexCount = Topology.VertexHandles.Count();

        // A pass drops every group's first vertex from its later iterations, so a pair that could only merge
        // once a neighbouring pair had merged is not retried within the pass. Run whole passes until one
        // merges nothing, every pass starts from all the vertices again.
        for (var pass = 0; pass < 16; pass++)
        {
            var merged = MergeVerticesWithinDistance(Topology.VertexHandles.ToList(), maxDistance, averagePositions: false, out _);
            if (merged == 0)
            {
                break;
            }

            total += merged;
        }

        return total;
    }

    /// <summary>
    /// Merges the given vertices into groups of vertices lying within <paramref name="maxDistance"/> of each other
    /// </summary>
    /// <param name="originalVertices">Vertices to consider for merging.</param>
    /// <param name="maxDistance">Largest distance between two vertices that still get merged, negative merges all of them into one.</param>
    /// <param name="averagePositions">Move each merged vertex to the average of the positions it merged, otherwise the first vertex of a group keeps its position.</param>
    /// <param name="finalVertices">The vertices left over after merging.</param>
    /// <returns>Number of vertices merged away.</returns>
    public int MergeVerticesWithinDistance(IReadOnlyList<VertexHandle> originalVertices, float maxDistance, bool averagePositions, out List<VertexHandle> finalVertices)
    {
        finalVertices = [];
        var useDistance = maxDistance >= 0.0f;
        var distance = new Vector3(maxDistance, maxDistance, maxDistance);
        var maxIterations = 10;
        var maxDistanceSquared = useDistance ? (maxDistance * maxDistance) : float.MaxValue;

        var verticesToMerge = new List<VertexHandle>(originalVertices.Count);
        foreach (var hVertex in originalVertices)
        {
            if (hVertex.IsValid)
            {
                verticesToMerge.Add(hVertex);
            }
        }

        var numOriginalVertices = verticesToMerge.Count;
        if (numOriginalVertices < 2)
        {
            return 0;
        }

        var numTotalVerticesMerged = 0;

        finalVertices.EnsureCapacity(numOriginalVertices);

        // Assign the vertices to groups based on their positions. Each group will contain all of the
        // vertices within the specified maximum distance of the first vertex in the group.
        var verticesSortedByGroup = new List<VertexHandle>(numOriginalVertices);
        var groupVertexCounts = new int[numOriginalVertices];
        var groupOffsets = new int[numOriginalVertices];

        for (var iteration = 0; iteration < maxIterations; ++iteration)
        {
            // Stop if there are not at least two vertices left.
            var numVerticesToMerge = verticesToMerge.Count;
            if (numVerticesToMerge < 2)
            {
                break;
            }

            var vertexGroupAssignments = new List<int>(numVerticesToMerge);
            vertexGroupAssignments.AddRange(Enumerable.Repeat(-1, numVerticesToMerge));
            verticesSortedByGroup.Clear();

            var numGroups = 0;

            if (useDistance)
            {
                // Build an array of the positions specifically for the vertices
                // to merge instead of all the positions in the mesh
                var vertexPositions = new List<Vector3>(numVerticesToMerge);
                for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
                {
                    vertexPositions.Add(Positions[verticesToMerge[iVertex]]);
                }

                // Build a kd-tree of the vertex positions that we can use to find nearby vertices more efficiently
                var vertexPositionTree = new VertexKDTree();
                vertexPositionTree.BuildMidpoint(vertexPositions);

                for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
                {
                    // Check to see if the vertex has already been added to a group.
                    if (vertexGroupAssignments[iVertex] >= 0)
                    {
                        continue;
                    }

                    // If the vertex has not been assigned to a group assign it the next available group.
                    var hVertexA = verticesToMerge[iVertex];
                    var groupPosition = Positions[hVertexA];
                    var groupIndex = numGroups++;
                    vertexGroupAssignments[iVertex] = groupIndex;

                    // Set the index of the start of the group in the sorted vertex array.
                    groupOffsets[groupIndex] = verticesSortedByGroup.Count;

                    // Add the vertex to the sorted array
                    verticesSortedByGroup.Add(hVertexA);

                    // Search the the rest of the vertices to see if there are any which have not yet been
                    // assigned a group that are close enough to the current vertex to be grouped with it.
                    var groupMin = groupPosition - distance;
                    var groupMax = groupPosition + distance;
                    var verticesInBox = vertexPositionTree.FindVertsInBox(groupMin, groupMax);
                    var numVerticesInBox = verticesInBox.Count;

                    // There are some cases where the behavior of merging is order dependent, ideally it
                    // wouldn't be, but it is due to the constraint of not being able to connect more
                    // than two faces at a single vertex by merging. So to maintain the same behavior
                    // as the old approach we need to add the vertices in the order they were supplied
                    // in the input list.
                    verticesInBox.Sort();

                    for (var iVertexInBox = 0; iVertexInBox < numVerticesInBox; ++iVertexInBox)
                    {
                        var vertexIndexB = verticesInBox[iVertexInBox];
                        var vertexPosition = vertexPositions[vertexIndexB];

                        if (Vector3.DistanceSquared(vertexPosition, groupPosition) < maxDistanceSquared)
                        {
                            if (vertexGroupAssignments[vertexIndexB] < 0)
                            {
                                var hVertexB = verticesToMerge[vertexIndexB];
                                verticesSortedByGroup.Add(hVertexB);
                                vertexGroupAssignments[vertexIndexB] = groupIndex;
                            }
                        }
                    }

                    // Compute the number of vertices that were assigned to the group
                    groupVertexCounts[groupIndex] = verticesSortedByGroup.Count - groupOffsets[groupIndex];
                }
            }
            else
            {
                // If not using the distance just add all the vertices to a single group for merging
                numGroups = 1;
                verticesSortedByGroup = new(verticesToMerge);

                for (var i = 0; i < vertexGroupAssignments.Count; i++)
                {
                    vertexGroupAssignments[i] = 0;
                }

                groupVertexCounts[0] = numVerticesToMerge;
                groupOffsets[0] = 0;
            }

            var groupsMergedVertexCount = new int[numGroups]; // Number of vertices in each group that were successfully merged
            var groupsSumPosition = new Vector3[numGroups]; // Average position of the vertices in the group that were merged
            var groupsTargetVertex = new VertexHandle[numGroups]; // Target vertex with which other vertices in the group should be merged.

            for (var iGroup = 0; iGroup < numGroups; ++iGroup)
            {
                var groupVertexOffset = groupOffsets[iGroup];
                var hFirstVertex = verticesSortedByGroup[groupVertexOffset];

                groupsMergedVertexCount[iGroup] = 1;
                groupsTargetVertex[iGroup] = hFirstVertex;
                groupsSumPosition[iGroup] = Positions[hFirstVertex];

                // Clear the first vertex in the group, it does not need to be merged.
                verticesSortedByGroup[groupVertexOffset] = VertexHandle.Invalid;
            }

            // Merge all of the vertices in each group. Multiple iterations are done until all of the vertices
            // in all of the groups have been merged or until no vertices were merged in the previous iteration.
            for (var iGroupPass = 0; iGroupPass < numVerticesToMerge; ++iGroupPass)
            {
                var numMerged = 0;
                var numUnmerged = 0;

                for (var iGroup = 0; iGroup < numGroups; ++iGroup)
                {
                    var groupVertexCount = groupVertexCounts[iGroup];
                    if (groupVertexCount < 2)
                    {
                        continue;
                    }

                    var groupVertexOffset = groupOffsets[iGroup];
                    var numUnmergedInGroup = 0;

                    var hTargetVertex = groupsTargetVertex[iGroup];

                    for (var iVertex = 1; iVertex < groupVertexCount; ++iVertex)
                    {
                        var hMergeVertex = verticesSortedByGroup[groupVertexOffset + iVertex];
                        if (!hMergeVertex.IsValid)
                        {
                            continue;
                        }

                        // Get the position of the vertex to be merged before
                        // merging it, which will delete the vertex.
                        var mergeVertexPosition = Positions[hMergeVertex];

                        // If averaging positions, set the merge interpolation parameter to 0.5f,
                        // otherwise set it to 1.0 so that the data of the merge vertex is preserved.
                        var param = averagePositions ? 0.5f : 1.0f;

                        if (MergeVertices(hTargetVertex, hMergeVertex, param, maxDistanceSquared, out var hNewVertex))
                        {
                            // Add the position of the vertex to the group sum position
                            groupsSumPosition[iGroup] += mergeVertexPosition;
                            groupsMergedVertexCount[iGroup] += 1;

                            // Update the merged vertex of the group
                            groupsTargetVertex[iGroup] = hNewVertex;

                            // Update the target vertex to be the new vertex since the target vertex has
                            // be removed, if we don't update the target, then there is no way to merge
                            // the remaining vertices in this pass.
                            hTargetVertex = hNewVertex;

                            // Set the original vertex in the group to invalid so we
                            // don't try to merge it again in subsequent passes.
                            verticesSortedByGroup[groupVertexOffset + iVertex] = VertexHandle.Invalid;

                            ++numMerged;
                        }
                        else
                        {
                            ++numUnmerged;
                            ++numUnmergedInGroup;
                        }
                    }

                    // If all of the vertices in the group were merged mark the group as not having any
                    // vertices so that it is not touched in any future iterations.
                    if (numUnmergedInGroup == 0)
                    {
                        groupVertexCounts[iGroup] = -1;
                    }
                }

                if ((numUnmerged == 0) || (numMerged == 0))
                {
                    break;
                }
            }

            // Set the merged vertex positions to the average position
            var numVerticesMerged = 0;
            for (var iGroup = 0; iGroup < numGroups; ++iGroup)
            {
                if (averagePositions)
                {
                    var hVertex = groupsTargetVertex[iGroup];
                    if (hVertex.IsValid)
                    {
                        var averagePosition = groupsSumPosition[iGroup] / groupsMergedVertexCount[iGroup];
                        Positions[hVertex] = averagePosition;
                    }
                }

                var numVerticesMergedInGroup = groupsMergedVertexCount[iGroup];
                if (numVerticesMergedInGroup > 1)
                {
                    numVerticesMerged += numVerticesMergedInGroup;
                }
            }

            // Add the merged vertices from the groups
            for (var iGroup = 0; iGroup < numGroups; ++iGroup)
            {
                if (groupsTargetVertex[iGroup].IsValid)
                {
                    finalVertices.Add(groupsTargetVertex[iGroup]);
                }
            }

            // Build the remaining list of vertices to merge
            verticesToMerge.Clear();
            for (var iVertex = 0; iVertex < numVerticesToMerge; ++iVertex)
            {
                if (verticesSortedByGroup[iVertex].IsValid)
                {
                    verticesToMerge.Add(verticesSortedByGroup[iVertex]);
                }
            }

            numTotalVerticesMerged += numVerticesMerged;
        }

        // Add all of the vertices which were not merged
        finalVertices.AddRange(verticesToMerge);

        return numTotalVerticesMerged;
    }

    /// <summary>
    /// Merges two vertices into one where the topology allows it, interpolating the position by param (0 keeps the
    /// first vertex, 1 the second). Connected vertices collapse their edge.
    /// </summary>
    public bool MergeVertices(VertexHandle hVertexA, VertexHandle hVertexB, float param, out VertexHandle hOutNewVertex)
        => MergeVertices(hVertexA, hVertexB, param, float.MaxValue, out hOutNewVertex);

    // Merges two vertices, interpolating the position by param (0 keeps the first vertex, 1 the second).
    // Ported from S&box PolygonMesh.MergeVertices, with the bowtie guard added.
    private bool MergeVertices(VertexHandle hVertexA, VertexHandle hVertexB, float param, float maxDistanceSquared, out VertexHandle hOutNewVertex)
    {
        // If there is an edge connecting the vertices, just call edge collapse so that
        // the proper interpolation is done for the face vertices of the merged edge.
        var hEdge = HalfEdgeMesh.FindHalfEdgeConnectingVertices(hVertexA, hVertexB);
        if (hEdge != HalfEdgeHandle.Invalid)
        {
            return CollapseEdge(hEdge, param, out hOutNewVertex);
        }

        if (WouldLeaveBowtie(hVertexA, hVertexB, maxDistanceSquared))
        {
            hOutNewVertex = VertexHandle.Invalid;
            return false;
        }

        // Interpolate the data on the two vertices and store a copy before they are destroyed
        var newVertex = Vector3.Lerp(Positions[hVertexA], Positions[hVertexB], param);

        // Merge the two vertices and create a new one with
        // the interpolated values of the original vertices.
        if (Topology.MergeVertices(hVertexA, hVertexB, out hOutNewVertex))
        {
            Positions[hOutNewVertex] = newVertex;
            return true;
        }

        return false;
    }

    // Two unconnected vertices that don't share a neighbour either get merged by splicing their boundary loops
    // through one vertex, a bowtie. Along a seam that is only the first step: merging the neighbouring pair
    // turns the two fans into one. Where two objects merely touch at a point it is permanent, and would glue
    // them together. So allow it only when a neighbouring pair across the seam coincides as well.
    private bool WouldLeaveBowtie(VertexHandle hVertexA, VertexHandle hVertexB, float maxDistanceSquared)
    {
        var hOpenA = SingleOpenOutgoingEdge(hVertexA);
        var hOpenB = SingleOpenOutgoingEdge(hVertexB);

        if (!hOpenA.IsValid || !hOpenB.IsValid)
        {
            return false; // the topology refuses these anyway
        }

        // a pair of open edges connects them, the topology collapses a temporary triangle instead of splicing
        if (HalfEdgeMesh.FindHalfEdgeConnectingVertices(hOpenA.Vertex, hVertexB).IsValid
            || HalfEdgeMesh.FindHalfEdgeConnectingVertices(hOpenB.Vertex, hVertexA).IsValid)
        {
            return false;
        }

        // seam check: A's outgoing open edge ends where B's incoming open edge starts, or the mirror of that
        var hIncomingA = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hOpenA);
        var hIncomingB = HalfEdgeMesh.FindPreviousEdgeInFaceLoop(hOpenB);

        return !(Coincide(hOpenA.Vertex, hIncomingB.OppositeEdge.Vertex)
            || Coincide(hOpenB.Vertex, hIncomingA.OppositeEdge.Vertex));

        bool Coincide(VertexHandle a, VertexHandle b)
            => a.IsValid && b.IsValid && Vector3.DistanceSquared(Positions[a], Positions[b]) <= maxDistanceSquared;
    }

    // the open half edge leaving a vertex when it has exactly one, as the topology requires for merging
    private static HalfEdgeHandle SingleOpenOutgoingEdge(VertexHandle hVertex)
    {
        var hOpen = HalfEdgeHandle.Invalid;

        if (!HalfEdgeMesh.GetOutgoingHalfEdgesConnectedToVertex(hVertex, out var edges))
        {
            return hOpen;
        }

        foreach (var hEdge in edges)
        {
            if (hEdge.Face == FaceHandle.Invalid)
            {
                if (hOpen.IsValid)
                {
                    return HalfEdgeHandle.Invalid;
                }

                hOpen = hEdge;
            }
        }

        return hOpen;
    }

    // Collapses an edge into one vertex, interpolating the position by param. Ported from S&box PolygonMesh.CollapseEdge.
    private bool CollapseEdge(HalfEdgeHandle hHalfEdgeA, float param, out VertexHandle hOutNewVertex)
    {
        var hHalfEdgeB = HalfEdgeMesh.GetOppositeHalfEdge(hHalfEdgeA);

        // Get the vertices connected to the edge and average the values
        var hVertexA = HalfEdgeMesh.GetEndVertexConnectedToEdge(hHalfEdgeB);
        var hVertexB = HalfEdgeMesh.GetEndVertexConnectedToEdge(hHalfEdgeA);

        var newVertex = Vector3.Lerp(Positions[hVertexA], Positions[hVertexB], param);
        var hEdge = Topology.GetFullEdgeForHalfEdge(hHalfEdgeA);
        var removed = Topology.CollapseEdge(hEdge, out hOutNewVertex, out _);

        if (hOutNewVertex != VertexHandle.Invalid)
        {
            Positions[hOutNewVertex] = newVertex;
        }

        return removed;
    }
}

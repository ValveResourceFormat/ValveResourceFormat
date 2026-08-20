
using System.Diagnostics;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

partial class HalfEdgeMesh
{
    /// <summary>
    /// Counts the edges bordering a face.
    /// </summary>
    public static int ComputeNumEdgesInFace(FaceHandle hFace)
    {
        var nNumEdges = 0;

        if (hFace.IsValid)
        {
            var hCurrentEdge = hFace.Edge;
            do
            {
                ++nNumEdges;
                hCurrentEdge = hCurrentEdge.NextEdge;
            }
            while (hCurrentEdge != hFace.Edge);
        }

        return nNumEdges;
    }

    /// <summary>
    /// Returns the edge a face loop starts at.
    /// </summary>
    public static HalfEdgeHandle GetFirstEdgeInFaceLoop(FaceHandle hFace)
    {
        if (!hFace.IsValid)
            return HalfEdgeHandle.Invalid;

        return hFace.Edge;
    }

    /// <summary>
    /// Returns the next edge around the face.
    /// </summary>
    public static HalfEdgeHandle GetNextEdgeInFaceLoop(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
            return HalfEdgeHandle.Invalid;

        return hEdge.NextEdge;
    }

    /// <summary>
    /// Returns the previous edge around the face.
    /// </summary>
    public static HalfEdgeHandle FindPreviousEdgeInFaceLoop(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
            return HalfEdgeHandle.Invalid;

        var hCurrentEdge = hEdge;
        do
        {
            if (hCurrentEdge.NextEdge == hEdge)
                return hCurrentEdge;

            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hEdge);

        return HalfEdgeHandle.Invalid;
    }

    /// <summary>
    /// Returns the edge of a face that ends at the given vertex.
    /// </summary>
    public static HalfEdgeHandle FindEdgeConnectedToFaceEndingAtVertex(FaceHandle hFace, VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
            return HalfEdgeHandle.Invalid;

        var hFirstEdge = GetFirstEdgeInVertexLoop(hVertex);
        var hOutgoingEdge = hFirstEdge;

        do
        {
            var hIncomingEdge = hOutgoingEdge.OppositeEdge;
            if (hIncomingEdge.Face == hFace)
                return hIncomingEdge;

            hOutgoingEdge = hIncomingEdge.NextEdge;
        }
        while (hOutgoingEdge != hFirstEdge);

        return HalfEdgeHandle.Invalid;
    }

    /// <summary>
    /// Returns the edge shared by two faces.
    /// </summary>
    public HalfEdgeHandle FindEdgeConnectingFaces(FaceHandle hFaceA, FaceHandle hFaceB)
    {
        if (!hFaceA.IsValid)
            return HalfEdgeHandle.Invalid;

        var hCurrentEdge = hFaceA.Edge;
        do
        {
            if (hCurrentEdge.OppositeEdge.Face == hFaceB)
                return GetFullEdgeForHalfEdge(hCurrentEdge);

            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hFaceA.Edge);

        return HalfEdgeHandle.Invalid;
    }

    /// <summary>
    /// Collects the half edges bounding a face.
    /// </summary>
    public static bool GetHalfEdgesConnectedToFace(FaceHandle hFace, out HalfEdgeHandle[]? hEdges)
    {
        hEdges = null;

        if (!hFace.IsValid)
            return false;

        var nNumEdges = ComputeNumEdgesInFace(hFace);
        if (nNumEdges <= 0)
            return false;

        hEdges = new HalfEdgeHandle[nNumEdges];

        int i = 0;
        var hEdge = hFace.Edge;
        do
        {
            hEdges[i++] = hEdge;
            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);

        return hEdges.Length == nNumEdges;
    }

    /// <summary>
    /// Collects the full edges bounding a face.
    /// </summary>
    public bool GetFullEdgesConnectedToFace(FaceHandle hFace, out List<HalfEdgeHandle> edges)
    {
        edges = new List<HalfEdgeHandle>();

        if (!hFace.IsValid)
            return false;

        int nNumEdges = ComputeNumEdgesInFace(hFace);
        if (nNumEdges <= 0)
            return false;

        edges.EnsureCapacity(nNumEdges);

        var hEdge = hFace.Edge;
        do
        {
            edges.Add(GetFullEdgeForHalfEdge(hEdge));
            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);

        Debug.Assert(edges.Count == nNumEdges);
        return edges.Count == nNumEdges;
    }

    /// <summary>
    /// Collects the corner vertices of a face.
    /// </summary>
    public static bool GetVerticesConnectedToFace(FaceHandle hFace, out VertexHandle[]? vertices)
    {
        vertices = null;

        if (!hFace.IsValid)
            return false;

        int nNumVertices = ComputeNumEdgesInFace(hFace);
        if (nNumVertices <= 0)
            return false;

        vertices = new VertexHandle[nNumVertices];

        var i = 0;
        var hCurrentEdge = hFace.Edge;
        do
        {
            vertices[i++] = hCurrentEdge.Vertex;
            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hFace.Edge);

        return i == nNumVertices;
    }

    /// <summary>
    /// Collects every vertex touched by a set of faces.
    /// </summary>
    public static void FindVerticesConnectedToFaces(IReadOnlyList<FaceHandle> pFaceList, int nNumFaces, out VertexHandle[] outVertices)
    {
        var uniqueVertices = new Dictionary<VertexHandle, int>();
        var i = 0;
        for (var iFace = 0; iFace < nNumFaces; ++iFace)
        {
            var hFace = pFaceList[iFace];
            var hStartEdge = GetFirstEdgeInFaceLoop(hFace);

            if (!hStartEdge.IsValid)
                continue;

            var hCurrentEdge = hStartEdge;
            do
            {
                if (!uniqueVertices.ContainsKey(hCurrentEdge.Vertex))
                    uniqueVertices.Add(hCurrentEdge.Vertex, i++);

                hCurrentEdge = GetNextEdgeInFaceLoop(hCurrentEdge);
            }
            while (hCurrentEdge != hStartEdge);
        }

        outVertices = new VertexHandle[uniqueVertices.Count];
        foreach (var hVertex in uniqueVertices)
            outVertices[hVertex.Value] = hVertex.Key;
    }

    /// <summary>
    /// Collects the full edges of a set of faces, along with how many of those faces each edge borders.
    /// </summary>
    public void FindFullEdgesConnectedToFaces(IReadOnlyList<FaceHandle> pFaceList, int nNumFaces, out HalfEdgeHandle[] pOutEdgeList, out int[] pOutEdgeFaceCounts)
    {
        var nNumTotalFaceEdges = 0;
        for (var iFace = 0; iFace < nNumFaces; ++iFace)
        {
            nNumTotalFaceEdges += ComputeNumEdgesInFace(pFaceList[iFace]);
        }

        var uniqueEdges = new Dictionary<HalfEdgeHandle, int>(nNumTotalFaceEdges);

        for (var iFace = 0; iFace < nNumFaces; ++iFace)
        {
            var hFace = pFaceList[iFace];
            var hStartEdge = GetFirstEdgeInFaceLoop(hFace);
            if (hStartEdge == HalfEdgeHandle.Invalid)
                continue;

            var hCurrentEdge = hStartEdge;
            do
            {
                var hFullEdge = GetFullEdgeForHalfEdge(hCurrentEdge);
                if (!uniqueEdges.ContainsKey(hFullEdge))
                    uniqueEdges.Add(hFullEdge, uniqueEdges.Count);

                hCurrentEdge = GetNextEdgeInFaceLoop(hCurrentEdge);
            }
            while (hCurrentEdge != hStartEdge);
        }

        var nNumUniqueEdges = uniqueEdges.Count;
        pOutEdgeList = new HalfEdgeHandle[nNumUniqueEdges];

        foreach (var pair in uniqueEdges)
        {
            pOutEdgeList[pair.Value] = pair.Key;
        }

        pOutEdgeFaceCounts = new int[nNumUniqueEdges];

        for (var iFace = 0; iFace < nNumFaces; ++iFace)
        {
            var hFace = pFaceList[iFace];
            var hStartEdge = GetFirstEdgeInFaceLoop(hFace);
            if (hStartEdge == HalfEdgeHandle.Invalid)
                continue;

            var hCurrentEdge = hStartEdge;
            do
            {
                var hFullEdge = GetFullEdgeForHalfEdge(hCurrentEdge);
                if (uniqueEdges.TryGetValue(hFullEdge, out var nIndex))
                {
                    pOutEdgeFaceCounts[nIndex]++;
                }

                hCurrentEdge = GetNextEdgeInFaceLoop(hCurrentEdge);
            }
            while (hCurrentEdge != hStartEdge);
        }
    }

    /// <summary>
    /// Collects the faces sharing an edge with the given face.
    /// </summary>
    public static bool GetFacesConnectedToFace(FaceHandle hFace, out List<FaceHandle> faces)
    {
        faces = [];

        if (!hFace.IsValid)
            return false;

        int edgeCount = ComputeNumEdgesInFace(hFace);
        if (edgeCount <= 0)
            return false;

        faces.EnsureCapacity(edgeCount);

        var hEdge = hFace.Edge;
        do
        {
            var hOppositeEdge = hEdge.OppositeEdge;
            if (hOppositeEdge.Face != FaceHandle.Invalid)
            {
                faces.Add(hOppositeEdge.Face);
            }

            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);

        return true;
    }

    /// <summary>
    /// Returns the faces of a set whose every edge borders two faces.
    /// </summary>
    public static void FindClosedFaces(IReadOnlyList<FaceHandle> faceList, out List<FaceHandle> outClosedFaces)
    {
        outClosedFaces = new List<FaceHandle>(faceList.Count);

        for (int iFace = 0; iFace < faceList.Count; ++iFace)
        {
            bool isClosed = true;

            var hFace = faceList[iFace];
            var hStartEdge = GetFirstEdgeInFaceLoop(hFace);
            var hCurrentEdge = hStartEdge;

            do
            {
                var pEdge = hCurrentEdge;
                var pOppositeEdge = pEdge.OppositeEdge;

                if (!pOppositeEdge.Face.IsValid)
                {
                    isClosed = false;
                    break;
                }

                hCurrentEdge = GetNextEdgeInFaceLoop(hCurrentEdge);

            } while (hCurrentEdge != hStartEdge);

            if (isClosed)
            {
                outClosedFaces.Add(hFace);
            }
        }
    }

    /// <summary>
    /// Collects the edges on the boundary of a set of faces.
    /// </summary>
    public void FindBoundaryEdgesConnectedToFaces(IReadOnlyList<FaceHandle> faceList, int numFaces, out List<HalfEdgeHandle> outBoundaryEdges)
    {
        FindFullEdgesConnectedToFaces(faceList, numFaces, out var allConnectedEdges, out var edgeFaceCounts);

        int numConnectedEdges = allConnectedEdges.Length;

        outBoundaryEdges = new List<HalfEdgeHandle>(numConnectedEdges);

        for (int iEdge = 0; iEdge < numConnectedEdges; ++iEdge)
        {
            if (edgeFaceCounts[iEdge] != 2)
            {
                outBoundaryEdges.Add(allConnectedEdges[iEdge]);
            }
        }
    }
}

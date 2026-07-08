using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

internal struct Vertex
{
    public int Edge { get; set; } // Half edge emanating from the vertex

    public static Vertex Invalid => new() { Edge = -1 };
}

internal struct Face
{
    public int Edge { get; set; } // One of the edges opposite to the face

    public static Face Invalid => new() { Edge = -1 };
}

internal struct HalfEdge
{
    public int Vertex { get; set; } // Vertex at the end of the edge
    public int OppositeEdge { get; set; } // Half edge which runs the opposite direction from this edge
    public int NextEdge { get; set; } // Next half edge in the edge loop around the face to which this edge belongs
    public int Face { get; set; } // Face to which the half edge belongs

    public static HalfEdge Invalid => new()
    {
        Vertex = -1,
        OppositeEdge = -1,
        NextEdge = -1,
        Face = -1,
    };
}

internal enum EdgeConnectivityType
{
    Open,   // Edge is open (connected to 1 face)
    Closed, // Edge is closed (connected to 2 faces)
    Any,    // Edge is open or closed (connected to 1 or 2 faces)
}

public enum ComponentConnectivityType
{
    None,   // None of the edges in the set are connected to any other edges
    Mixed,  // Some of the edges are connected but not all edges are connected to a single group
    List,   // All of the edges are connected in a single list
    Loop,   // All of the edges are connected in a single closed loop
    Tree,   // All of the edges are connected in a single group, but there a branches in the connection
}

// Handles are basically just wrappers over raw integer indices into topology data lists (verts, half edges, faces)
// It offers a nicer and saver way to interact with the data structure

public readonly record struct VertexHandle
{
    public int Index { get; private init; }
    internal HalfEdgeMesh? Mesh { get; private init; }

    internal VertexHandle(int index, HalfEdgeMesh mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsVertexAllocated(Index);
    public static VertexHandle Invalid => new(-1, null);

    public HalfEdgeHandle Edge
    {
        get => new(Mesh is null ? -1 : Mesh[this].Edge, Mesh);
        set => Mesh?.SetVertexEdge(this, value);
    }

    public override string ToString() => $"{Index}";
}

public readonly record struct FaceHandle
{
    public int Index { get; private init; }
    internal HalfEdgeMesh Mesh { get; private init; }

    internal FaceHandle(int index, HalfEdgeMesh mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsFaceAllocated(Index);
    public static FaceHandle Invalid => new(-1, null);

    public HalfEdgeHandle Edge
    {
        get => new(Mesh is null ? -1 : Mesh[this].Edge, Mesh);
        set => Mesh?.SetFaceEdge(this, value);
    }

    public override string ToString() => $"{Index}";
}

public readonly record struct HalfEdgeHandle
{
    public int Index { get; private init; }
    internal HalfEdgeMesh Mesh { get; private init; }

    internal HalfEdgeHandle(int index, HalfEdgeMesh mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsHalfEdgeAllocated(Index);
    public static HalfEdgeHandle Invalid => new(-1, null);

    public VertexHandle Vertex
    {
        get => new(Mesh is null ? -1 : Mesh[this].Vertex, Mesh);
        set => Mesh?.SetEdgeVertex(this, value);
    }

    public HalfEdgeHandle OppositeEdge
    {
        get => new(Mesh is null ? -1 : Mesh[this].OppositeEdge, Mesh);
        set => Mesh?.SetEdgeOpposite(this, value);
    }

    public HalfEdgeHandle NextEdge
    {
        get => new(Mesh is null ? -1 : Mesh[this].NextEdge, Mesh);
        set => Mesh?.SetEdgeNext(this, value);
    }

    public FaceHandle Face
    {
        get => new(Mesh is null ? -1 : Mesh[this].Face, Mesh);
        set => Mesh?.SetEdgeFace(this, value);
    }

    public override string ToString() => $"{Index}";
}

/// <summary>
/// Half-Edge mesh typically used in Hammer.
/// </summary>
/// <remarks>
/// Taken from <see href="https://github.com/Facepunch/sbox-public/tree/master/engine/Sandbox.Engine/Scene/Components/Mesh/HalfEdgeMesh">Sbox</see>.
/// </remarks>
internal sealed partial class HalfEdgeMesh
{
    private ComponentList<Vertex> VertexList { get; set; } = new();
    private ComponentList<Face> FaceList { get; set; } = new();
    private ComponentList<HalfEdge> HalfEdgeList { get; set; } = new();

    public Action<HalfEdgeHandle, HalfEdgeHandle> OnCopyFaceVertexData { get; set; }
    public Action<HalfEdgeHandle> OnClearFaceVertexData { get; set; }

    internal int VertexCount => VertexList.Count;
    internal int FaceCount => FaceList.Count;
    internal int HalfEdgeCount => HalfEdgeList.Count;

    private bool IsVertexInMesh(VertexHandle hVertex) => hVertex.IsValid;

    private VertexHandle AllocateVertex(Vertex vertex, int sourceIndex = -1) => new(VertexList.Allocate(vertex, sourceIndex), this);
    private FaceHandle AllocateFace(Face face, int sourceIndex = -1) => new(FaceList.Allocate(face, sourceIndex), this);
    private HalfEdgeHandle AllocateHalfEdge(HalfEdge halfEdge, int sourceIndex = -1) => new(HalfEdgeList.Allocate(halfEdge, sourceIndex), this);

    public bool IsVertexAllocated(VertexHandle hVertex) => VertexList.IsAllocated(hVertex.Index);
    public bool IsFaceAllocated(FaceHandle hFace) => FaceList.IsAllocated(hFace.Index);
    public bool IsHalfEdgeAllocated(HalfEdgeHandle hHalfEdge) => HalfEdgeList.IsAllocated(hHalfEdge.Index);

    internal bool IsVertexAllocated(int index) => VertexList.IsAllocated(index);
    internal bool IsFaceAllocated(int index) => FaceList.IsAllocated(index);
    internal bool IsHalfEdgeAllocated(int index) => HalfEdgeList.IsAllocated(index);

    public IEnumerable<VertexHandle> VertexHandles => VertexList.ActiveList.Select(i => new VertexHandle(i, this));
    public IEnumerable<FaceHandle> FaceHandles => FaceList.ActiveList.Select(i => new FaceHandle(i, this));
    public IEnumerable<HalfEdgeHandle> HalfEdgeHandles => HalfEdgeList.ActiveList.Select(i => new HalfEdgeHandle(i, this));

    public VertexHandle AddVertex() => AllocateVertex(Vertex.Invalid);

    public void AppendComponentsFromMesh(HalfEdgeMesh sourceMesh,
        out Dictionary<VertexHandle, VertexHandle> newVertices,
        out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
        out Dictionary<FaceHandle, FaceHandle> newFaces)
    {
        newVertices = new();
        newHalfEdges = new();
        newFaces = new();

        foreach (var hVertex in sourceMesh.VertexHandles)
        {
            var hNewVertex = AllocateVertex(Vertex.Invalid);
            newVertices.Add(hVertex, hNewVertex);
        }

        foreach (var hFace in sourceMesh.FaceHandles)
        {
            var hNewFace = AllocateFace(Face.Invalid);
            newFaces.Add(hFace, hNewFace);
        }

        foreach (var hHalfEdge in sourceMesh.HalfEdgeHandles)
        {
            var hNewHalfEdge = AllocateHalfEdge(HalfEdge.Invalid);
            newHalfEdges.Add(hHalfEdge, hNewHalfEdge);
        }

        foreach (var pair in newVertices)
        {
            var hVertex = pair.Key;
            var hNewVertex = pair.Value;

            if (newHalfEdges.TryGetValue(hVertex.Edge, out var hEdge))
                hNewVertex.Edge = hEdge;
        }

        foreach (var pair in newFaces)
        {
            var hFace = pair.Key;
            var hNewFace = pair.Value;

            if (newHalfEdges.TryGetValue(hFace.Edge, out var hEdge))
                hNewFace.Edge = hEdge;
        }

        foreach (var pair in newHalfEdges)
        {
            var hHalfEdge = pair.Key;
            var hNewHalfEdge = pair.Value;

            if (newVertices.TryGetValue(hHalfEdge.Vertex, out var hVertex))
                hNewHalfEdge.Vertex = hVertex;

            if (newHalfEdges.TryGetValue(hHalfEdge.OppositeEdge, out var hOppositeEdge))
                hNewHalfEdge.OppositeEdge = hOppositeEdge;

            if (newHalfEdges.TryGetValue(hHalfEdge.NextEdge, out var hNextEdge))
                hNewHalfEdge.NextEdge = hNextEdge;

            if (newFaces.TryGetValue(hHalfEdge.Face, out var hFace))
                hNewHalfEdge.Face = hFace;
        }
    }

    public IEnumerable<VertexHandle> AddVertices(int count)
    {
        int vertexCount = VertexCount;
        VertexList.AllocateMultiple(count, Vertex.Invalid);

        for (int i = 0; i < count; i++)
            yield return new(vertexCount + i, this);
    }

    public FaceHandle AddFace(params VertexHandle[] hVertices)
    {
        if (!AddFace(hVertices, out var hFace))
            return FaceHandle.Invalid;

        return hFace;
    }

    public bool AddFace(out FaceHandle hOutFace, params VertexHandle[] hVertices)
    {
        if (!AddFace(hVertices, out hOutFace))
            return false;

        return true;
    }

    public int ComputeNumOpenEdgesInVertexLoop(VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
            return 0;

        var nNumOpenEdges = 0;

        // Iterate over all of the edges emanating from the vertex and determine 
        // if they are connected to a face. If not increment the open edge count.
        var hEdge = hVertex.Edge;
        if (hVertex.Edge == HalfEdgeHandle.Invalid)
            return 0;

        do
        {
            if (hEdge.Face == FaceHandle.Invalid)
                ++nNumOpenEdges;

            hEdge = GetOppositeHalfEdge(hEdge).NextEdge;
        }
        while (hEdge != hVertex.Edge);

        return nNumOpenEdges;
    }

    public HalfEdgeHandle FindOpenOppositeEdgeInVertexLoop(VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
            return HalfEdgeHandle.Invalid;

        if (hVertex.Edge == HalfEdgeHandle.Invalid)
            return HalfEdgeHandle.Invalid;

        var hCurrentEdge = hVertex.Edge;

        do
        {
            var hOppositeEdge = GetOppositeHalfEdge(hCurrentEdge);
            if (hOppositeEdge.Face == FaceHandle.Invalid)
                return hOppositeEdge;

            hCurrentEdge = hOppositeEdge.NextEdge;
        }
        while (hCurrentEdge != hVertex.Edge);

        return HalfEdgeHandle.Invalid;
    }

    public HalfEdgeHandle FindOppositeEdgeWithNextEdgeInVertexLoop(VertexHandle hVertex, HalfEdgeHandle hNextEdge)
    {
        if (!hVertex.IsValid)
            return HalfEdgeHandle.Invalid;

        if (hVertex.Edge == HalfEdgeHandle.Invalid)
            return HalfEdgeHandle.Invalid;

        var hCurrentEdge = hVertex.Edge;

        do
        {
            var hOppositeEdge = GetOppositeHalfEdge(hCurrentEdge);
            if (hOppositeEdge.NextEdge == hNextEdge)
                return hOppositeEdge;

            hCurrentEdge = hOppositeEdge.NextEdge;
        }
        while (hCurrentEdge != hVertex.Edge);

        return HalfEdgeHandle.Invalid;
    }

    private HalfEdgeHandle ConstructHalfEdgePair(VertexHandle hVertexA, VertexHandle hVertexB, int sourceIndexA = -1, int sourceIndexB = -1)
    {
        // Should never be trying to add an edge which already exists
        Debug.Assert(!FindHalfEdgeConnectingVertices(hVertexA, hVertexB).IsValid, "Trying to add an edge which already exists!");
        Debug.Assert(!FindHalfEdgeConnectingVertices(hVertexB, hVertexA).IsValid, "Trying to add an edge which already exists!");

        // Construct both halves of the half edge pair
        if (AllocateHalfEdgePair(out var hEdgeAB, out var hEdgeBA, sourceIndexA, sourceIndexB))
        {
            hEdgeAB.Vertex = hVertexB;
            hEdgeBA.Vertex = hVertexA;
        }

        return hEdgeAB;
    }

    private bool IsHalfEdgeInMesh(HalfEdgeHandle hHalfEdge)
    {
        return hHalfEdge.IsValid;
    }

    public HalfEdgeHandle FindConnectedHalfEdgeInSet(HalfEdgeHandle hEdge, IReadOnlyList<HalfEdgeHandle> pEdges, int nNumEdges)
    {
        if (!hEdge.IsValid)
            return HalfEdgeHandle.Invalid;

        var hStartEdge = hEdge.NextEdge;
        var hCurrentEdge = hStartEdge;

        do
        {
            // Is the edge in the provided list
            for (int iEdge = 0; iEdge < nNumEdges; ++iEdge)
            {
                if (hCurrentEdge == pEdges[iEdge])
                    return hCurrentEdge;
            }

            // Get the next edge connected to the vertex
            hCurrentEdge = GetNextEdgeInVertexLoop(hCurrentEdge);
        }
        while (hCurrentEdge != hStartEdge);

        return HalfEdgeHandle.Invalid;
    }

    private bool AllocateHalfEdgePair(out HalfEdgeHandle hHalfEdgeA, out HalfEdgeHandle hHalfEdgeB, int sourceIndexA = -1, int sourceIndexB = -1)
    {
        int halfEdgeCount = HalfEdgeCount;

        var edgeA = new HalfEdge
        {
            Vertex = -1,
            OppositeEdge = halfEdgeCount + 1,
            NextEdge = halfEdgeCount + 1,
            Face = -1,
        };

        var edgeB = new HalfEdge
        {
            Vertex = -1,
            OppositeEdge = halfEdgeCount,
            NextEdge = halfEdgeCount,
            Face = -1,
        };

        hHalfEdgeA = AllocateHalfEdge(edgeA, sourceIndexA);
        hHalfEdgeB = AllocateHalfEdge(edgeB, sourceIndexB);

        return true;
    }

    private void AttachEdgesToFace(FaceHandle hFace, HalfEdgeHandle[] pAllEdges, int nNumEdges)
    {
        Debug.Assert(hFace.IsValid);

        if (!hFace.IsValid)
            return;

        var hEdge = pAllEdges[nNumEdges - 1];
        for (int iEdge = 0; iEdge < nNumEdges; ++iEdge)
        {
            var hNextEdge = pAllEdges[iEdge];
            var hOppositeEdge = GetOppositeHalfEdge(hEdge);
            var hNextOppositeEdge = GetOppositeHalfEdge(hNextEdge);

            Debug.Assert(hNextOppositeEdge.Vertex == hEdge.Vertex);

            // Assign the face to the edge. It is important this is done first
            // so that this edge doesn't turn up in the open edge search.
            hEdge.Face = hFace;

            if (hOppositeEdge.Face == FaceHandle.Invalid)
            {
                HalfEdgeHandle hInsertAfterEdge;

                if (hNextOppositeEdge.Face != FaceHandle.Invalid)
                {
                    hInsertAfterEdge = FindOppositeEdgeWithNextEdgeInVertexLoop(hEdge.Vertex, hNextEdge);
                }
                else
                {
                    hInsertAfterEdge = FindOpenOppositeEdgeInVertexLoop(hEdge.Vertex);
                }

                if (hInsertAfterEdge != HalfEdgeHandle.Invalid)
                {
                    hEdge.NextEdge = hInsertAfterEdge.NextEdge;
                    hInsertAfterEdge.NextEdge = hEdge.OppositeEdge;
                }
            }

            // Check to see if the vertex has been assigned an edge yet, if not assign it the next 
            // edge, since the edge assigned to a vertex is the edge starting at the vertex.
            var hVertex = hEdge.Vertex;
            if (hVertex.Edge == HalfEdgeHandle.Invalid)
            {
                hVertex.Edge = hNextEdge;
            }

            if (hNextOppositeEdge.Face == FaceHandle.Invalid)
            {
                hNextOppositeEdge.NextEdge = hEdge.NextEdge;
                hEdge.NextEdge = hNextEdge;
            }

            Debug.Assert(hEdge.NextEdge == hNextEdge);

            hEdge = hNextEdge;
        }

        // Make the face points to the last edge so that that when a face is created 
        // the vertex ordering will match the order of the provided vertices.
        hFace.Edge = pAllEdges[nNumEdges - 1];

        Debug.Assert(CheckFaceIntegrity(hFace));
    }

    private bool CheckFaceIntegrity(FaceHandle hFace, bool bAssert = true)
    {
        Debug.Assert(hFace.IsValid || (bAssert == false));
        if (!hFace.IsValid)
            return false;

        var hFirstEdge = hFace.Edge;
        Debug.Assert(hFirstEdge.IsValid || (bAssert == false));
        if (!hFirstEdge.IsValid)
            return false;

        var hEdge = hFace.Edge;
        do
        {
            Debug.Assert(hEdge.IsValid || (bAssert == false));
            if (!hEdge.IsValid)
                return false;

            Debug.Assert(hEdge.Face == hFace || (bAssert == false));
            if (hEdge.Face != hFace)
                return false;

            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);

        return true;
    }

    private bool AddFace(VertexHandle[] pVerticesA, out FaceHandle hFace)
    {
        hFace = FaceHandle.Invalid;

        var nNumVertices = pVerticesA.Length;
        if (nNumVertices < 3)
            return false;

        var pEdgeHandles = new HalfEdgeHandle[nNumVertices];
        var pVerticesB = new VertexHandle[nNumVertices];
        for (int iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            pVerticesB[iVertex] = pVerticesA[(iVertex + 1) % nNumVertices];
        }

        // Find all of the existing edges and ensure they are
        // open and make sure that the new edges can be added.
        for (int iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            pEdgeHandles[iVertex] = FindHalfEdgeConnectingVertices(pVerticesA[iVertex], pVerticesB[iVertex]);

            var pEdge = pEdgeHandles[iVertex];
            if (pEdge.IsValid)
            {
                // Cannot construct a face using an edge which is already in use by another face
                if (pEdge.Face != FaceHandle.Invalid)
                {
                    return false;
                }
            }
            else if (pVerticesB[iVertex].Edge != HalfEdgeHandle.Invalid)
            {
                int nNumOpenEdges = ComputeNumOpenEdgesInVertexLoop(pVerticesB[iVertex]);

                // If a new edge is being added to a vertex which already has edges attached there
                // must be at least on open edge, otherwise there is nowhere to insert the new edge.
                if (nNumOpenEdges == 0)
                {
                    return false;
                }

                // If there are two open edges then we must ensure that the next edge being added is an
                // existing edge, otherwise it will be ambiguous as to where the face is to be added.
                if (nNumOpenEdges >= 2)
                {
                    if (!FindHalfEdgeConnectingVertices(pVerticesB[iVertex], pVerticesB[(iVertex + 1) % nNumVertices]).IsValid)
                    {
                        return false;
                    }
                }
            }
        }

        // If two neighboring edges are existing edges they must be directly 
        // connected, they cannot have additional edges between them.
        for (int iEdge = 0; iEdge < nNumVertices; ++iEdge)
        {
            var hEdge = pEdgeHandles[iEdge];
            var hNextEdge = pEdgeHandles[(iEdge + 1) % nNumVertices];

            if (hEdge.IsValid && hNextEdge.IsValid)
            {
                if (hEdge.NextEdge != hNextEdge)
                {
                    return false;
                }
            }
        }

        hFace = AllocateFace(Face.Invalid);

        // Create the new edges
        for (int iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            if (!pEdgeHandles[iVertex].IsValid)
            {
                // Check for an existing edge connecting the vertices in the opposite direction,
                // this may occur if there is an interior edge in the face.
                for (int iEdge = 0; iEdge < iVertex; ++iEdge)
                {
                    GetVerticesConnectedToHalfEdge(pEdgeHandles[iEdge], out var hVertexA, out var hVertexB);
                    if ((hVertexA == pVerticesB[iVertex]) && (hVertexB == pVerticesA[iVertex]))
                    {
                        pEdgeHandles[iVertex] = pEdgeHandles[iEdge].OppositeEdge;
                    }
                }

                if (!pEdgeHandles[iVertex].IsValid)
                {
                    pEdgeHandles[iVertex] = ConstructHalfEdgePair(pVerticesA[iVertex], pVerticesB[iVertex]);
                }

                Debug.Assert(pEdgeHandles[iVertex].IsValid);
            }
        }

        // Attach the edges to the face
        AttachEdgesToFace(hFace, pEdgeHandles, nNumVertices);

        return true;
    }

    private struct FaceEdgePair
    {
        public FaceHandle Face;
        public HalfEdgeHandle IncomingEdge;
        public HalfEdgeHandle OutgoingEdge;
    };

    private void FreeHalfEdge(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
            return;

        this[hHalfEdge] = HalfEdge.Invalid;

        HalfEdgeList.Deallocate(hHalfEdge.Index);
    }

    private void FreeHalfEdgePair(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
            return;

        FreeHalfEdge(hHalfEdge.OppositeEdge);
        FreeHalfEdge(hHalfEdge);
    }

    private void FreeFace(FaceHandle hFace)
    {
        if (!hFace.IsValid)
            return;

        this[hFace] = Face.Invalid;
        FaceList.Deallocate(hFace.Index);
    }

    private void ClearEdgeData(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
            return;

        OnClearFaceVertexData?.Invoke(hEdge);
    }

    internal void SetEdgeVertex(HalfEdgeHandle hEdge, VertexHandle hVertex)
    {
        var halfEdge = this[hEdge];
        halfEdge.Vertex = hVertex.Index;
        this[hEdge] = halfEdge;
    }

    internal void SetEdgeOpposite(HalfEdgeHandle hEdge, HalfEdgeHandle hOpposite)
    {
        var halfEdge = this[hEdge];
        halfEdge.OppositeEdge = hOpposite.Index;
        this[hEdge] = halfEdge;
    }

    internal void SetEdgeNext(HalfEdgeHandle hEdge, HalfEdgeHandle hNext)
    {
        var halfEdge = this[hEdge];
        halfEdge.NextEdge = hNext.Index;
        this[hEdge] = halfEdge;
    }

    internal void SetEdgeFace(HalfEdgeHandle hEdge, FaceHandle hFace)
    {
        var halfEdge = this[hEdge];
        halfEdge.Face = hFace.Index;
        this[hEdge] = halfEdge;
    }

    internal void SetVertexEdge(VertexHandle hVertex, HalfEdgeHandle hEdge)
    {
        var vertex = this[hVertex];
        vertex.Edge = hEdge.Index;
        this[hVertex] = vertex;
    }

    internal void SetFaceEdge(FaceHandle hFace, HalfEdgeHandle hEdge)
    {
        var face = this[hFace];
        face.Edge = hEdge.Index;
        this[hFace] = face;
    }

    // Removes an interior edge, merging the two faces connected to it into a single face
    // The face of the given half edge survives, the opposite face is freed together with the edge pair
    public bool DissolveEdge(HalfEdgeHandle hEdge, out FaceHandle hOutFace)
    {
        hOutFace = FaceHandle.Invalid;

        if (!hEdge.IsValid)
            return false;

        var hEdgeA = hEdge;
        var hEdgeB = GetOppositeHalfEdge(hEdge);

        var hFaceA = hEdgeA.Face; // kept
        var hFaceB = hEdgeB.Face; // merged into face A

        // must be an interior edge connecting two distinct faces
        if (hFaceA == FaceHandle.Invalid || hFaceB == FaceHandle.Invalid || hFaceA == hFaceB)
            return false;

        // faces connected by more than one edge can't be merged by dissolving a single edge,
        // that would leave the second shared edge as a degenerate interior edge
        var sharedEdges = 0;
        var hCurrentEdge = hFaceA.Edge;
        do
        {
            if (hCurrentEdge.OppositeEdge.Face == hFaceB)
            {
                ++sharedEdges;
            }

            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hFaceA.Edge);

        if (sharedEdges != 1)
            return false;

        var hPrevA = FindPreviousEdgeInFaceLoop(hEdgeA);
        var hPrevB = FindPreviousEdgeInFaceLoop(hEdgeB);
        var hNextA = hEdgeA.NextEdge;
        var hNextB = hEdgeB.NextEdge;

        // move all of face B's edges over to face A
        hCurrentEdge = hNextB;
        do
        {
            hCurrentEdge.Face = hFaceA;
            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hEdgeB);

        // splice the two face loops together, bypassing the dissolved edge pair
        hPrevA.NextEdge = hNextB;
        hPrevB.NextEdge = hNextA;

        // repoint the end vertices if their outgoing edge is one of the freed half edges
        var hVertexA = hEdgeB.Vertex; // hEdgeA emanates from this vertex
        var hVertexB = hEdgeA.Vertex; // hEdgeB emanates from this vertex

        if (hVertexA.Edge == hEdgeA)
            hVertexA.Edge = hNextB;

        if (hVertexB.Edge == hEdgeB)
            hVertexB.Edge = hNextA;

        // repoint the surviving face if its edge is being freed
        if (hFaceA.Edge == hEdgeA)
            hFaceA.Edge = hPrevA;

        ClearEdgeData(hEdgeA);
        ClearEdgeData(hEdgeB);

        FreeHalfEdgePair(hEdgeA);
        FreeFace(hFaceB);

        hOutFace = hFaceA;
        return true;
    }

    public Vertex this[VertexHandle hVertex]
    {
        get => hVertex.Mesh is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count ? VertexList[hVertex.Index] : Vertex.Invalid;
        private set
        {
            if (hVertex.Mesh is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count)
                VertexList[hVertex.Index] = value;
        }
    }

    public Face this[FaceHandle hFace]
    {
        get => hFace.Mesh is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count ? FaceList[hFace.Index] : Face.Invalid;
        private set
        {
            if (hFace.Mesh is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count)
                FaceList[hFace.Index] = value;
        }
    }

    public HalfEdge this[HalfEdgeHandle hEdge]
    {
        get => hEdge.Mesh is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count ? HalfEdgeList[hEdge.Index] : HalfEdge.Invalid;
        private set
        {
            if (hEdge.Mesh is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count)
                HalfEdgeList[hEdge.Index] = value;
        }
    }
}


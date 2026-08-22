using System.Diagnostics;
using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

/// <summary>
/// Topology of one vertex.
/// </summary>
public struct Vertex
{
    /// <summary>
    /// Half edge emanating from the vertex.
    /// </summary>
    public int Edge { get; set; }

    /// <summary>
    /// A vertex that points at no edge.
    /// </summary>
    public static Vertex Invalid => new() { Edge = -1 };
}

/// <summary>
/// Topology of one face.
/// </summary>
public struct Face
{
    /// <summary>
    /// One of the edges opposite to the face.
    /// </summary>
    public int Edge { get; set; }

    /// <summary>
    /// A face that points at no edge.
    /// </summary>
    public static Face Invalid => new() { Edge = -1 };
}

/// <summary>
/// Topology of one half edge.
/// </summary>
public struct HalfEdge
{
    /// <summary>
    /// Vertex at the end of the edge.
    /// </summary>
    public int Vertex { get; set; }

    /// <summary>
    /// Half edge which runs the opposite direction from this edge.
    /// </summary>
    public int OppositeEdge { get; set; }

    /// <summary>
    /// Next half edge in the edge loop around the face to which this edge belongs.
    /// </summary>
    public int NextEdge { get; set; }

    /// <summary>
    /// Face to which the half edge belongs.
    /// </summary>
    public int Face { get; set; }

    /// <summary>
    /// A half edge that points at nothing.
    /// </summary>
    public static HalfEdge Invalid => new()
    {
        Vertex = -1,
        OppositeEdge = -1,
        NextEdge = -1,
        Face = -1,
    };
}

/// <summary>
/// How many faces an edge is allowed to border.
/// </summary>
public enum EdgeConnectivityType
{
    /// <summary>Edge is open (connected to 1 face).</summary>
    Open,

    /// <summary>Edge is closed (connected to 2 faces).</summary>
    Closed,

    /// <summary>Edge is open or closed (connected to 1 or 2 faces).</summary>
    Any,
}

/// <summary>
/// Shape a set of edges forms once their connections are followed.
/// </summary>
public enum ComponentConnectivityType
{
    /// <summary>None of the edges in the set are connected to any other edges.</summary>
    None,

    /// <summary>Some of the edges are connected but not all edges are connected to a single group.</summary>
    Mixed,

    /// <summary>All of the edges are connected in a single list.</summary>
    List,

    /// <summary>All of the edges are connected in a single closed loop.</summary>
    Loop,

    /// <summary>All of the edges are connected in a single group, but there a branches in the connection.</summary>
    Tree,
}

// Handles are basically just wrappers over raw integer indices into topology data lists (verts, half edges, faces)
// It offers a nicer and safer way to interact with the data structure

/// <summary>
/// A vertex of a specific mesh, addressed through the mesh it came from.
/// </summary>
public readonly record struct VertexHandle
{
    /// <summary>
    /// Index of the vertex within the mesh.
    /// </summary>
    public int Index { get; private init; }

    internal HalfEdgeMesh? Mesh { get; private init; }

    internal VertexHandle(int index, HalfEdgeMesh? mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    /// <summary>
    /// Whether the handle still addresses a live vertex.
    /// </summary>
    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsVertexAllocated(Index);

    /// <summary>
    /// A handle that addresses no vertex.
    /// </summary>
    public static VertexHandle Invalid => new(-1, null);

    /// <summary>
    /// Gets or sets the half edge emanating from this vertex.
    /// </summary>
    public HalfEdgeHandle Edge
    {
        get => new(Mesh is null ? -1 : Mesh[this].Edge, Mesh);
        set => Mesh?.SetVertexEdge(this, value);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Index}";
}

/// <summary>
/// A face of a specific mesh, addressed through the mesh it came from.
/// </summary>
public readonly record struct FaceHandle
{
    /// <summary>
    /// Index of the face within the mesh.
    /// </summary>
    public int Index { get; private init; }

    internal HalfEdgeMesh? Mesh { get; private init; }

    internal FaceHandle(int index, HalfEdgeMesh? mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    /// <summary>
    /// Whether the handle still addresses a live face.
    /// </summary>
    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsFaceAllocated(Index);

    /// <summary>
    /// A handle that addresses no face.
    /// </summary>
    public static FaceHandle Invalid => new(-1, null);

    /// <summary>
    /// Gets or sets one of the half edges bordering this face.
    /// </summary>
    public HalfEdgeHandle Edge
    {
        get => new(Mesh is null ? -1 : Mesh[this].Edge, Mesh);
        set => Mesh?.SetFaceEdge(this, value);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Index}";
}

/// <summary>
/// A half edge of a specific mesh, addressed through the mesh it came from.
/// </summary>
public readonly record struct HalfEdgeHandle
{
    /// <summary>
    /// Index of the half edge within the mesh.
    /// </summary>
    public int Index { get; private init; }

    internal HalfEdgeMesh? Mesh { get; private init; }

    internal HalfEdgeHandle(int index, HalfEdgeMesh? mesh)
    {
        Index = index;
        Mesh = index >= 0 ? mesh : null;
    }

    /// <summary>
    /// Whether the handle still addresses a live half edge.
    /// </summary>
    public bool IsValid => Index >= 0 && Mesh is not null && Mesh.IsHalfEdgeAllocated(Index);

    /// <summary>
    /// A handle that addresses no half edge.
    /// </summary>
    public static HalfEdgeHandle Invalid => new(-1, null);

    /// <summary>
    /// Gets or sets the vertex at the end of this edge.
    /// </summary>
    public VertexHandle Vertex
    {
        get => new(Mesh is null ? -1 : Mesh[this].Vertex, Mesh);
        set => Mesh?.SetEdgeVertex(this, value);
    }

    /// <summary>
    /// Gets or sets the half edge running the opposite direction.
    /// </summary>
    public HalfEdgeHandle OppositeEdge
    {
        get => new(Mesh is null ? -1 : Mesh[this].OppositeEdge, Mesh);
        set => Mesh?.SetEdgeOpposite(this, value);
    }

    /// <summary>
    /// Gets or sets the next half edge in the loop around this edge's face.
    /// </summary>
    public HalfEdgeHandle NextEdge
    {
        get => new(Mesh is null ? -1 : Mesh[this].NextEdge, Mesh);
        set => Mesh?.SetEdgeNext(this, value);
    }

    /// <summary>
    /// Gets or sets the face this half edge borders.
    /// </summary>
    public FaceHandle Face
    {
        get => new(Mesh is null ? -1 : Mesh[this].Face, Mesh);
        set => Mesh?.SetEdgeFace(this, value);
    }

    /// <inheritdoc/>
    public override string ToString() => $"{Index}";
}

/// <summary>
/// Half-Edge mesh typically used in Hammer.
/// </summary>
/// <remarks>
/// Taken from <see href="https://github.com/Facepunch/sbox-public/tree/master/engine/Sandbox.Engine/Scene/Components/Mesh/HalfEdgeMesh">Sbox</see>.
/// </remarks>
public partial class HalfEdgeMesh
{
    private ComponentList<Vertex> VertexList { get; set; } = new();
    private ComponentList<Face> FaceList { get; set; } = new();
    private ComponentList<HalfEdge> HalfEdgeList { get; set; } = new();

    /// <summary>
    /// Called when corner data must follow a half edge, with the source edge first and the destination second.
    /// </summary>
    public Action<HalfEdgeHandle, HalfEdgeHandle>? OnCopyFaceVertexData { get; set; }

    /// <summary>
    /// Called when a half edge loses its corner data.
    /// </summary>
    public Action<HalfEdgeHandle>? OnClearFaceVertexData { get; set; }

    internal int VertexCount => VertexList.Count;
    internal int FaceCount => FaceList.Count;
    internal int HalfEdgeCount => HalfEdgeList.Count;

    private static bool IsVertexInMesh(VertexHandle hVertex) => hVertex.IsValid;

    private VertexHandle AllocateVertex(Vertex vertex, int sourceIndex = -1) => new(VertexList.Allocate(vertex, sourceIndex), this);
    private FaceHandle AllocateFace(Face face, int sourceIndex = -1) => new(FaceList.Allocate(face, sourceIndex), this);
    private HalfEdgeHandle AllocateHalfEdge(HalfEdge halfEdge, int sourceIndex = -1) => new(HalfEdgeList.Allocate(halfEdge, sourceIndex), this);

    /// <summary>
    /// Whether the vertex slot is still in use.
    /// </summary>
    /// <param name="hVertex">Vertex to test.</param>
    public bool IsVertexAllocated(VertexHandle hVertex) => VertexList.IsAllocated(hVertex.Index);

    /// <summary>
    /// Whether the face slot is still in use.
    /// </summary>
    /// <param name="hFace">Face to test.</param>
    public bool IsFaceAllocated(FaceHandle hFace) => FaceList.IsAllocated(hFace.Index);

    /// <summary>
    /// Whether the half edge slot is still in use.
    /// </summary>
    /// <param name="hHalfEdge">Half edge to test.</param>
    public bool IsHalfEdgeAllocated(HalfEdgeHandle hHalfEdge) => HalfEdgeList.IsAllocated(hHalfEdge.Index);

    internal bool IsVertexAllocated(int index) => VertexList.IsAllocated(index);
    internal bool IsFaceAllocated(int index) => FaceList.IsAllocated(index);
    internal bool IsHalfEdgeAllocated(int index) => HalfEdgeList.IsAllocated(index);

    /// <summary>
    /// Handles of every live vertex.
    /// </summary>
    public IEnumerable<VertexHandle> VertexHandles => VertexList.ActiveList.Select(i => new VertexHandle(i, this));

    /// <summary>
    /// Handles of every live face.
    /// </summary>
    public IEnumerable<FaceHandle> FaceHandles => FaceList.ActiveList.Select(i => new FaceHandle(i, this));

    /// <summary>
    /// Handles of every live half edge.
    /// </summary>
    public IEnumerable<HalfEdgeHandle> HalfEdgeHandles => HalfEdgeList.ActiveList.Select(i => new HalfEdgeHandle(i, this));

    /// <summary>
    /// Adds one vertex that borders no edge yet.
    /// </summary>
    public VertexHandle AddVertex() => AllocateVertex(Vertex.Invalid);

    /// <summary>
    /// Copies every component of another mesh into this one, reporting the handle each source component landed on.
    /// </summary>
    /// <param name="sourceMesh">Mesh to copy from.</param>
    /// <param name="newVertices">Source vertex to new vertex.</param>
    /// <param name="newHalfEdges">Source half edge to new half edge.</param>
    /// <param name="newFaces">Source face to new face.</param>
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

    /// <summary>
    /// Copies a set of faces of another mesh into this one, together with their half edges (boundary twins
    /// included) and vertices, reporting the handle each source component landed on. The faces should form
    /// whole islands connected through edges: a half edge whose twin's face is not in the set becomes a
    /// boundary edge, a vertex shared with faces outside the set is duplicated.
    /// </summary>
    public void AppendComponentsFromMesh(HalfEdgeMesh sourceMesh,
        IReadOnlyCollection<FaceHandle> faces,
        out Dictionary<VertexHandle, VertexHandle> newVertices,
        out Dictionary<HalfEdgeHandle, HalfEdgeHandle> newHalfEdges,
        out Dictionary<FaceHandle, FaceHandle> newFaces)
    {
        newVertices = new();
        newHalfEdges = new();
        newFaces = new();

        foreach (var hFace in faces)
        {
            newFaces.Add(hFace, AllocateFace(Face.Invalid));
        }

        // half edges of the faces and their twins, allocated in pairs so twins stay adjacent, vertices as met
        foreach (var hFace in faces)
        {
            var hEdge = hFace.Edge;
            do
            {
                if (!newHalfEdges.ContainsKey(hEdge))
                {
                    var hOpposite = hEdge.OppositeEdge;
                    var hNewEdge = AllocateHalfEdge(HalfEdge.Invalid);
                    var hNewOpposite = AllocateHalfEdge(HalfEdge.Invalid);
                    newHalfEdges.Add(hEdge, hNewEdge);
                    newHalfEdges.Add(hOpposite, hNewOpposite);
                }

                if (!newVertices.ContainsKey(hEdge.Vertex))
                {
                    newVertices.Add(hEdge.Vertex, AllocateVertex(Vertex.Invalid));
                }

                hEdge = hEdge.NextEdge;
            }
            while (hEdge != hFace.Edge);
        }

        // A vertex may have edges outside the set when the copied faces only touch other faces at that vertex
        // (a bowtie). The copy then gets its own vertex, pointing at one of the copied edges, and the boundary
        // loop is closed up along the copied fan instead of leaving through the other fan.
        foreach (var (hVertex, hNewVertex) in newVertices)
        {
            if (!newHalfEdges.TryGetValue(hVertex.Edge, out var hNewEdge))
            {
                hNewEdge = newHalfEdges[FindNextOutgoingEdgeInSet(hVertex.Edge, newHalfEdges)];
            }

            hNewVertex.Edge = hNewEdge;
        }

        foreach (var (hFace, hNewFace) in newFaces)
        {
            hNewFace.Edge = newHalfEdges[hFace.Edge];
        }

        foreach (var (hHalfEdge, hNewHalfEdge) in newHalfEdges)
        {
            hNewHalfEdge.Vertex = newVertices[hHalfEdge.Vertex];
            hNewHalfEdge.OppositeEdge = newHalfEdges[hHalfEdge.OppositeEdge];
            hNewHalfEdge.Face = newFaces.TryGetValue(hHalfEdge.Face, out var hFace) ? hFace : FaceHandle.Invalid;

            if (newHalfEdges.TryGetValue(hHalfEdge.NextEdge, out var hNextEdge))
            {
                hNewHalfEdge.NextEdge = hNextEdge;
            }
            else
            {
                // only a boundary half edge can leave the set, its next edge is the open edge of the
                // other fan at the end vertex; rotating on from there reaches the copied fan at its open edge
                hNewHalfEdge.NextEdge = newHalfEdges[FindNextOutgoingEdgeInSet(hHalfEdge.NextEdge, newHalfEdges)];
            }
        }
    }

    // Rotates around the start vertex of the given outgoing half edge (the vertex loop order, which enters
    // each fan at its open outgoing edge) until it reaches a half edge in the set.
    private static HalfEdgeHandle FindNextOutgoingEdgeInSet(HalfEdgeHandle hOutgoingEdge, Dictionary<HalfEdgeHandle, HalfEdgeHandle> set)
    {
        var hCurrent = hOutgoingEdge;
        do
        {
            hCurrent = hCurrent.OppositeEdge.NextEdge;
        }
        while (hCurrent != hOutgoingEdge && !set.ContainsKey(hCurrent));

        if (!set.ContainsKey(hCurrent))
        {
            throw new InvalidOperationException("The copied faces don't own any edge at one of their vertices.");
        }

        return hCurrent;
    }

    /// <summary>
    /// Adds several vertices that border no edge yet.
    /// </summary>
    /// <param name="count">How many vertices to add.</param>
    public IEnumerable<VertexHandle> AddVertices(int count)
    {
        int vertexCount = VertexCount;
        VertexList.AllocateMultiple(count, Vertex.Invalid);

        for (int i = 0; i < count; i++)
            yield return new(vertexCount + i, this);
    }

    /// <summary>
    /// Adds a face bounded by the given vertices, in order.
    /// </summary>
    /// <param name="hVertices">Corner vertices of the new face.</param>
    /// <returns>The new face, or <see cref="FaceHandle.Invalid"/> when the face would break the mesh.</returns>
    public FaceHandle AddFace(params VertexHandle[] hVertices)
    {
        if (!AddFace(hVertices, out var hFace))
            return FaceHandle.Invalid;

        return hFace;
    }

    /// <summary>
    /// Adds a face bounded by the given vertices, in order.
    /// </summary>
    /// <param name="hOutFace">The new face, set only when this returns true.</param>
    /// <param name="hVertices">Corner vertices of the new face.</param>
    /// <returns>Whether the face was added.</returns>
    public bool AddFace(out FaceHandle hOutFace, params VertexHandle[] hVertices)
    {
        if (!AddFace(hVertices, out hOutFace))
            return false;

        return true;
    }

    /// <summary>
    /// Counts the edges around a vertex that border no face.
    /// </summary>
    /// <param name="hVertex">Vertex whose edge loop is walked.</param>
    public static int ComputeNumOpenEdgesInVertexLoop(VertexHandle hVertex)
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

    /// <summary>
    /// Finds an edge pointing at a vertex that borders no face.
    /// </summary>
    /// <param name="hVertex">Vertex whose edge loop is walked.</param>
    /// <returns>The open edge, or <see cref="HalfEdgeHandle.Invalid"/> when the vertex is closed.</returns>
    public static HalfEdgeHandle FindOpenOppositeEdgeInVertexLoop(VertexHandle hVertex)
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

    /// <summary>
    /// Finds the edge pointing at a vertex whose next edge is the given one.
    /// </summary>
    /// <param name="hVertex">Vertex whose edge loop is walked.</param>
    /// <param name="hNextEdge">Edge the result must lead into.</param>
    /// <returns>The matching edge, or <see cref="HalfEdgeHandle.Invalid"/> when there is none.</returns>
    public static HalfEdgeHandle FindOppositeEdgeWithNextEdgeInVertexLoop(VertexHandle hVertex, HalfEdgeHandle hNextEdge)
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

    private static bool IsHalfEdgeInMesh(HalfEdgeHandle hHalfEdge)
    {
        return hHalfEdge.IsValid;
    }

    /// <summary>
    /// Walks the vertex loop from an edge and returns the first edge that appears in the given set.
    /// </summary>
    /// <param name="hEdge">Edge to start from.</param>
    /// <param name="pEdges">Set to look in.</param>
    /// <param name="nNumEdges">How many entries of <paramref name="pEdges"/> to consider.</param>
    /// <returns>The connected edge, or <see cref="HalfEdgeHandle.Invalid"/> when none is in the set.</returns>
    public static HalfEdgeHandle FindConnectedHalfEdgeInSet(HalfEdgeHandle hEdge, IReadOnlyList<HalfEdgeHandle> pEdges, int nNumEdges)
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

    private static void AttachEdgesToFace(FaceHandle hFace, HalfEdgeHandle[] pAllEdges, int nNumEdges)
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

        // Make the face point to the last edge so that when a face is created
        // the vertex ordering will match the order of the provided vertices.
        hFace.Edge = pAllEdges[nNumEdges - 1];

        Debug.Assert(CheckFaceIntegrity(hFace));
    }

    private static bool CheckFaceIntegrity(FaceHandle hFace, bool bAssert = true)
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
                // must be at least an open edge, otherwise there is nowhere to insert the new edge.
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
    /// <summary>
    /// Merges the two faces sharing an edge. The face of the given half edge survives, the opposite face is
    /// freed together with the edge pair.
    /// </summary>
    /// <param name="hEdge">Edge to dissolve.</param>
    /// <param name="hOutFace">The surviving face, set only when this returns true.</param>
    /// <returns>Whether the edge was dissolved.</returns>
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

    /// <summary>
    /// Returns whichever half of a full edge borders no face, or an invalid handle when both sides have a face.
    /// </summary>
    public static HalfEdgeHandle GetOpenHalfEdgeFromFullEdge(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
            return HalfEdgeHandle.Invalid;

        if (hEdge.Face == FaceHandle.Invalid)
            return hEdge;

        var hOpposite = hEdge.OppositeEdge;
        if (hOpposite.Face == FaceHandle.Invalid)
            return hOpposite;

        return HalfEdgeHandle.Invalid;
    }

    private void DetachEdgeFromVertex(HalfEdgeHandle hEdge, bool bRemoveFreeVerts)
    {
        if (!hEdge.IsValid)
            return;

        if (hEdge.Vertex == VertexHandle.Invalid)
            return;

        // Get the opposite edge and the vertex from which the edge originates
        var hOppositeEdge = hEdge.OppositeEdge;
        var hVertex = hOppositeEdge.Vertex;

        // Determine if the this is the only edge attached to the vertex. If not remove the 
        // edge from the loop of edges going around the vertex, otherwise update the vertex 
        // edge reference and remove the vertex if remove free vertices is specified.
        if (hOppositeEdge.NextEdge != hEdge)
        {
            var hPreviousEdge = FindPreviousEdgeInVertexLoop(hEdge);
            Debug.Assert(hPreviousEdge.OppositeEdge.NextEdge == hEdge);

            var hPrevOpp = hPreviousEdge.OppositeEdge;
            hPrevOpp.NextEdge = hOppositeEdge.NextEdge;

            // Update the edge the vertex refers to to ensure 
            // it is not still referring to the that was detached.
            hVertex.Edge = hOppositeEdge.NextEdge;

            // Now make the opposite edge loop back
            hOppositeEdge.NextEdge = hEdge;
        }
        else
        {
            Debug.Assert(ComputeNumEdgesConnectedToVertex(hVertex) == 1);

            // If this is the only edge connected to the
            // vertex, the vertex should refer to it.
            Debug.Assert((hVertex.Edge == hEdge) || hVertex.Edge == HalfEdgeHandle.Invalid);

            // Set the vertex as being disconnected
            hVertex.Edge = HalfEdgeHandle.Invalid;

            // Remove the vertex from the mesh entirely if remove free vertices is true
            if (bRemoveFreeVerts)
            {
                RemoveVertex(hVertex, bRemoveFreeVerts);
            }
        }
    }

    private struct FaceEdgePair
    {
        public FaceHandle Face;
        public HalfEdgeHandle IncomingEdge;
        public HalfEdgeHandle OutgoingEdge;
    };

    /// <summary>
    /// Removes a vertex, merging the edges that met at it; triangles it was part of are removed. Optionally removes vertices left without edges.
    /// </summary>
    public bool RemoveVertex(VertexHandle hVertex, bool bRemoveFreeVerts)
    {
        if (!hVertex.IsValid)
            return false;

        var bValidEdge = hVertex.Edge != HalfEdgeHandle.Invalid;

        if (bValidEdge)
        {
            // Count the number of edges emanating from the vertex
            var nVertexNumEdges = 0;
            var hCurrentEdge = hVertex.Edge;
            HalfEdgeHandle hPreviousAdjEdge;
            do
            {
                ++nVertexNumEdges;
                hPreviousAdjEdge = hCurrentEdge.OppositeEdge;
                hCurrentEdge = hPreviousAdjEdge.NextEdge;
            }
            while (hCurrentEdge != hVertex.Edge);

            // Build a list of the pairs of edges going in and out of 
            // the specified vertex for each face connected to the vertex.
            var pFaceEdgePairs = new FaceEdgePair[nVertexNumEdges];
            var nNumPairs = 0;

            hCurrentEdge = hVertex.Edge;
            do
            {
                Debug.Assert(hPreviousAdjEdge.Vertex == hVertex);
                Debug.Assert(hPreviousAdjEdge.NextEdge == hCurrentEdge);
                Debug.Assert(hPreviousAdjEdge.Face == hCurrentEdge.Face);

                if (hCurrentEdge.Face != FaceHandle.Invalid)
                {
                    var faceEdgePair = pFaceEdgePairs[nNumPairs];
                    faceEdgePair.Face = hCurrentEdge.Face;
                    faceEdgePair.IncomingEdge = hPreviousAdjEdge;
                    faceEdgePair.OutgoingEdge = hCurrentEdge;
                    pFaceEdgePairs[nNumPairs] = faceEdgePair;
                    nNumPairs++;
                }

                hPreviousAdjEdge = hCurrentEdge.OppositeEdge;
                hCurrentEdge = hPreviousAdjEdge.NextEdge;
            }
            while (hCurrentEdge != hVertex.Edge);

            Debug.Assert(nNumPairs <= nVertexNumEdges);

            // If the face is a triangle removing the vertex would leave
            // it in an invalid state, so the whole face should be removed.
            for (var iPair = 0; iPair < nNumPairs; ++iPair)
            {
                var pair = pFaceEdgePairs[iPair];

                if (pair.OutgoingEdge.NextEdge.NextEdge == pair.IncomingEdge)
                {
                    RemoveFace(pair.Face, bRemoveFreeVerts);
                    pair.Face = FaceHandle.Invalid;
                    pFaceEdgePairs[iPair] = pair;
                }
            }

            // Replace the incoming and outgoing edges of the vertex with a 
            // single edge connecting the proceeding and following vertices. 
            for (var iPair = 0; iPair < nNumPairs; ++iPair)
            {
                var pair = pFaceEdgePairs[iPair];

                if (pair.Face != FaceHandle.Invalid)
                {
                    if (ReplaceFaceEdges(pair.Face, pair.IncomingEdge, pair.OutgoingEdge, bRemoveFreeVerts) == false)
                    {
                        RemoveFace(pair.Face, bRemoveFreeVerts);
                        pair.Face = FaceHandle.Invalid;
                        pFaceEdgePairs[iPair] = pair;
                    }
                }
            }
        }

        // If remove free vertices was specified, this vertex will
        // already have been removed if it was connected to anything.
        if (!bValidEdge || !bRemoveFreeVerts)
        {
            FreeVertex(hVertex);
        }

        return true;
    }

    private bool ReplaceFaceEdges(FaceHandle hFace, HalfEdgeHandle hIncomingEdge, HalfEdgeHandle hOutgoingEdge, bool bRemoveFreeVerts)
    {
        Debug.Assert(hFace.IsValid && hIncomingEdge.IsValid && hOutgoingEdge.IsValid);
        if (!hFace.IsValid || !hIncomingEdge.IsValid || !hOutgoingEdge.IsValid)
            return false;

        // Both edges must belong to the face
        Debug.Assert((hIncomingEdge.Face == hFace) && (hOutgoingEdge.Face == hFace));
        if ((hIncomingEdge.Face != hFace) || (hOutgoingEdge.Face != hFace))
            return false;

        // The outgoing edge must be the next edge in the loop from the incoming edge.
        Debug.Assert(hIncomingEdge.NextEdge == hOutgoingEdge);
        if (hIncomingEdge.NextEdge != hOutgoingEdge)
            return false;

        // Count the number of edges the face has, it must have more than 3 edges
        var nFaceNumEdges = ComputeNumEdgesInFace(hFace);
        Debug.Assert(nFaceNumEdges > 3);
        if (nFaceNumEdges <= 3)
            return false;

        var hIncomingOppositeEdge = hIncomingEdge.OppositeEdge;

        // The new edge must connect two different valid vertices
        var hVertexA = hIncomingOppositeEdge.Vertex;
        var hVertexB = hOutgoingEdge.Vertex;
        Debug.Assert(hVertexA.IsValid && hVertexB.IsValid && (hVertexA != hVertexB));
        if (!hVertexA.IsValid || !hVertexB.IsValid || (hVertexA == hVertexB))
            return false;

        // Build a list of all of the edges in the face excluding the ones that are going to be removed.
        var pEdgeList = new HalfEdgeHandle[nFaceNumEdges];
        int nNumEdges = 0;
        var hCurrentEdge = hOutgoingEdge.NextEdge;
        do
        {
            pEdgeList[nNumEdges++] = hCurrentEdge;
            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hIncomingEdge);
        Debug.Assert(nNumEdges == (nFaceNumEdges - 2));

        // Check to see if there is already a connecting edge. This can happen in the case where the 
        // edges are part of an open triangle loop or in the case where both the incoming and outgoing 
        // edges are internal to the face (both half edges of the pair reference the same face) when 
        // replacing the second face edge pair.
        var hConnectingEdge = FindHalfEdgeConnectingVertices(hVertexA, hVertexB);
        if (hConnectingEdge.IsValid)
        {
            // If both the incoming and outgoing edges both have a face attached this should be the case
            // where the edges are part of a triangle loop. If so the next edge of opposite edge of the
            // incoming edge should be the connecting edge and the next edge of the connecting should be
            // the opposite edge of the outgoing edge. This may occur if there is a face attached to one
            // or both to the vertices and is inside what appeared to be an triangle loop, making it not
            // actually a triangle loop.
            if ((hIncomingEdge.Face != FaceHandle.Invalid) &&
                 (hOutgoingEdge.Face != FaceHandle.Invalid))
            {
                if (hIncomingEdge.OppositeEdge.NextEdge != hConnectingEdge)
                    return false;

                if (hConnectingEdge.NextEdge != hOutgoingEdge.OppositeEdge)
                    return false;
            }
        }

        hIncomingEdge.Face = FaceHandle.Invalid;
        hOutgoingEdge.Face = FaceHandle.Invalid;

        if (hConnectingEdge.IsValid)
        {
            // Copy the data from the face vertex at the end of the outgoing edge to the face vertex at
            // the end of the connecting edge, since that is the vertex replacing the vertex at the end
            // of the outgoing edge.
            CopyFaceVertexData(hConnectingEdge, hOutgoingEdge);
        }
        else
        {
            // If an existing connecting edge was not found, construct a new edge which 
            // will replace the two removed edges and connect vertex a to vertex b.
            pEdgeList[nNumEdges++] = ConstructHalfEdgePair(hVertexA, hVertexB, hOutgoingEdge.Index, hIncomingEdge.OppositeEdge.Index);
            Debug.Assert(nNumEdges == (nFaceNumEdges - 1));
        }

        if (hIncomingEdge.OppositeEdge.Face == FaceHandle.Invalid)
        {
            RemoveHalfEdgePair(hIncomingEdge, bRemoveFreeVerts);
        }
        else
        {
            ClearEdgeData(hIncomingEdge);
        }

        if (hOutgoingEdge.OppositeEdge.Face == FaceHandle.Invalid)
        {
            RemoveHalfEdgePair(hOutgoingEdge, bRemoveFreeVerts);
        }
        else
        {
            ClearEdgeData(hOutgoingEdge);
        }

        if (hConnectingEdge.IsValid)
        {
            hConnectingEdge.Face = hFace;
            hFace.Edge = hConnectingEdge;
        }
        else
        {
            // Detach all of the remaining edges from the face.
            for (var iEdge = 0; iEdge < (nNumEdges - 1); ++iEdge)
            {
                var hEdge = pEdgeList[iEdge];
                hEdge.Face = FaceHandle.Invalid;

                if (hEdge.OppositeEdge.Face == FaceHandle.Invalid)
                {
                    DetachEdgeFromVertex(pEdgeList[iEdge], false);
                    DetachEdgeFromVertex(hEdge.OppositeEdge, false);
                }
            }

            hFace.Edge = HalfEdgeHandle.Invalid;

            // Attach all the edges to the face
            AttachEdgesToFace(hFace, pEdgeList, nNumEdges);

            for (var iEdge = 0; iEdge < (nNumEdges - 1); ++iEdge)
            {
                var hEdge = pEdgeList[iEdge];
                if (hEdge.OppositeEdge.Face == FaceHandle.Invalid)
                {
                    ClearEdgeData(hEdge.OppositeEdge);
                }
            }
        }

        // Remove any edges which are now loose edges in the face
        RemoveLooseEdgesInFace(hFace);

        return true;
    }

    /// <summary>
    /// Removes an edge and the faces attached to it. Optionally removes vertices left without edges.
    /// </summary>
    public bool RemoveEdge(HalfEdgeHandle hFullEdge, bool bRemoveFreeVerts)
    {
        return RemoveHalfEdgePair(hFullEdge, bRemoveFreeVerts);
    }

    private void CopyFaceVertexData(HalfEdgeHandle hDstHalfEdge, HalfEdgeHandle hSrcHalfEdge)
    {
        if (!hDstHalfEdge.IsValid)
            return;

        if (!hSrcHalfEdge.IsValid)
            return;

        OnCopyFaceVertexData?.Invoke(hDstHalfEdge, hSrcHalfEdge);
    }

    /// <summary>
    /// Removes a face and the edges only it used. Optionally removes vertices left without edges.
    /// </summary>
    public bool RemoveFace(FaceHandle hFace, bool bRemoveFreeVerts)
    {
        if (!hFace.IsValid)
            return false;

        var hFirstEdge = hFace.Edge;
        if (hFirstEdge.IsValid && (hFirstEdge.Face == hFace))
        {
            // Count the number of edges around polygon
            var nNumEdges = 0;
            var hEdge = hFace.Edge;
            do
            {
                hEdge = hEdge.NextEdge;
                ++nNumEdges;
            }
            while (hEdge != hFace.Edge);

            // Build the list of edges
            var pEdgeList = new HalfEdgeHandle[nNumEdges];
            var nEdge = 0;
            hEdge = hFace.Edge;
            do
            {
                pEdgeList[nEdge++] = hEdge;
                hEdge = hEdge.NextEdge;
            }
            while (hEdge != hFace.Edge);
            Debug.Assert(nEdge == nNumEdges);

            // Walk all of the edges of polygon, if an edge is only attached to the face being removed 
            // (its opposite edge is not attached to a face) the edge should be removed.
            for (var iEdge = 0; iEdge < nNumEdges; ++iEdge)
            {
                var hCurrentEdge = pEdgeList[iEdge];

                // Remove the edge's reference to this face.
                hCurrentEdge.Face = FaceHandle.Invalid;

                // If the opposite edge is open remove the edge since after removing this face it would no 
                // longer meet the requirement of all half edge pairs being attached to at least one face.
                // Note that if there is an interior edge it will appear in the list twice, once for 
                // each half edge, the first time it will remove the face from the half edge resulting in
                // it being removed when the second half edge is encountered.
                var hOppositeEdge = hCurrentEdge.OppositeEdge;
                if (hOppositeEdge.Face == FaceHandle.Invalid)
                {
                    RemoveHalfEdgePair(hCurrentEdge.OppositeEdge, bRemoveFreeVerts);
                }
            }
        }

        FreeFace(hFace);

        return true;
    }

    private bool RemoveHalfEdgePair(HalfEdgeHandle hEdge, bool bRemoveFreeVerts)
    {
        if (!hEdge.IsValid)
            return false;

        var hAdjEdge = hEdge.OppositeEdge;
        var hOppositeEdge = hAdjEdge;

        // Determine if the edge is a loose edge, in this case the face connected to the edge should not
        // be removed, but needs to be updated so that it doesn't refer to the edge once it is removed.
        var bLooseEdge = IsLooseEdge(GetFullEdgeForHalfEdge(hEdge));

        if ((hEdge.Face.IsValid || hOppositeEdge.Face.IsValid) && (bLooseEdge == false))
        {
            // Remove the faces attached to the edge and its opposite edge. Note this will
            // result in RemoveFace() calling RemoveEdge when no more faces are attached 
            // to the edge, so we don't actually remove the edge directly here.
            var hFace = hEdge.Face;
            var hAdjFace = hOppositeEdge.Face;
            RemoveFace(hFace, bRemoveFreeVerts);
            RemoveFace(hAdjFace, bRemoveFreeVerts);

            // Note: It is possible that the edge is corrupt and the face it refers to does not refer 
            // to it, in this case the edge may not have been removed along with the face, so free the
            // edge here if it is still in the mesh.
            RemoveHalfEdgePair(hEdge, bRemoveFreeVerts);
        }
        else
        {
            // If the edge is a loose edge which is connected to a face which will not be
            // removed make sure that face is not referring to this edge or its opposite edge
            var hFace = hEdge.Face;
            if (bLooseEdge && hFace.IsValid)
            {
                var hNextFaceEdge = hFace.Edge;
                while ((hNextFaceEdge == hEdge) || (hNextFaceEdge == hAdjEdge))
                {
                    hNextFaceEdge = hNextFaceEdge.NextEdge;

                    // If we have come full circle there and not found an edge which is not going
                    // to be removed stop. This means the face is invalid and should be removed.
                    if (hNextFaceEdge == hFace.Edge)
                    {
                        hNextFaceEdge = HalfEdgeHandle.Invalid;
                        break;
                    }
                }

                hFace.Edge = hNextFaceEdge;

                Debug.Assert(hFace.Edge != hEdge);
                Debug.Assert(hFace.Edge != hAdjEdge);

                if (hFace.Edge == HalfEdgeHandle.Invalid)
                {
                    RemoveFace(hEdge.Face, false);
                }
            }

            // Detach the edge and its opposite edge from the vertices they originate from.
            DetachEdgeFromVertex(hEdge, bRemoveFreeVerts);
            DetachEdgeFromVertex(hEdge.OppositeEdge, bRemoveFreeVerts);

            // Remove the edge and its opposite edge from the mesh. 
            // Note pEdge is invalid as soon as hEdge is removed
            FreeHalfEdgePair(hEdge);
        }

        return true;
    }

    private bool IsLooseEdge(HalfEdgeHandle hFullEdge)
    {
        GetHalfEdgesConnectedToFullEdge(hFullEdge, out var hHalfEdgeA, out var hHalfEdgeB);

        if ((this[hHalfEdgeA].OppositeEdge == this[hHalfEdgeA].NextEdge) ||
             (this[hHalfEdgeB].OppositeEdge == this[hHalfEdgeB].NextEdge))
            return true;

        return false;
    }

    private void RemoveLooseEdgesInFace(FaceHandle hFace)
    {
        var hEdgeToRemove = HalfEdgeHandle.Invalid;
        {
            if (hFace.IsValid)
            {
                hEdgeToRemove = FindFirstLooseEdgeInFaceLoop(hFace.Edge);
            }
        }

        while (hEdgeToRemove.IsValid)
        {
            RemoveHalfEdgePair(hEdgeToRemove, true);

            // Its possible that removing the edge above will result in removing the face if it was 
            // the last edge in the face loop. If so, there are no more edges to remove and we must 
            // stop because the face pointer may be invalid.
            if (!hFace.IsValid)
                break;

            hEdgeToRemove = FindFirstLooseEdgeInFaceLoop(hFace.Edge);
        }
    }

    private static HalfEdgeHandle FindFirstLooseEdgeInFaceLoop(HalfEdgeHandle hStartEdge)
    {
        if (hStartEdge.IsValid)
        {
            var hCurrentEdge = hStartEdge;
            do
            {
                if (hCurrentEdge.OppositeEdge == hCurrentEdge.NextEdge)
                    return hCurrentEdge;

                hCurrentEdge = hCurrentEdge.NextEdge;
            }
            while (hCurrentEdge != hStartEdge);
        }

        return HalfEdgeHandle.Invalid;
    }

    private void FreeVertex(VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
            return;

        this[hVertex] = Vertex.Invalid;
        VertexList.Deallocate(hVertex.Index);
    }

    /// <summary>
    /// Splits a face by adding an edge between the end vertices of two of its half edges.
    /// </summary>
    public bool AddEdgeToFace(HalfEdgeHandle hIncomingEdgeA, HalfEdgeHandle hIncomingEdgeB, out HalfEdgeHandle hOutNewEdge)
    {
        hOutNewEdge = HalfEdgeHandle.Invalid;

        if (!hIncomingEdgeA.IsValid || !hIncomingEdgeB.IsValid)
            return false;

        // Both edges must be connected to the same face
        var hFace = hIncomingEdgeA.Face;
        if (hIncomingEdgeB.Face != hFace)
            return false;

        if (!hFace.IsValid)
            return false;

        // Both edges cannot end at the same vertex 
        var hVertexA = hIncomingEdgeA.Vertex;
        var hVertexB = hIncomingEdgeB.Vertex;
        if (hVertexA == hVertexB)
            return false;

        // Make sure that an edge connecting the specified vertices does not already exist.
        if (FindFullEdgeConnectingVertices(hVertexA, hVertexB).IsValid)
            return false;

        // Create the new half edge pair
        if (AllocateHalfEdgePair(out var hNewEdgeAB, out var hNewEdgeBA, hIncomingEdgeB.Index, hIncomingEdgeA.Index) == false)
            return false;

        hNewEdgeAB.Vertex = hVertexB;
        hNewEdgeBA.Vertex = hVertexA;

        // Reconnect the edges
        hNewEdgeAB.NextEdge = hIncomingEdgeB.NextEdge;
        hNewEdgeBA.NextEdge = hIncomingEdgeA.NextEdge;
        hIncomingEdgeA.NextEdge = hNewEdgeAB;
        hIncomingEdgeB.NextEdge = hNewEdgeBA;

        // Assign new edge A to the existing face 
        hNewEdgeAB.Face = hFace;
        hFace.Edge = hNewEdgeAB;

        // Create the new face and assign it to all of 
        // the edges in the loop with new edge B.
        var hNewFace = AllocateFace(Face.Invalid, hFace.Index);
        if (hNewFace.IsValid)
        {
            hNewFace.Edge = hNewEdgeBA;
            var hNewFaceEdge = hNewFace.Edge;
            do
            {
                hNewFaceEdge.Face = hNewFace;
                hNewFaceEdge = hNewFaceEdge.NextEdge;
            }
            while (hNewFaceEdge != hNewFace.Edge);

            Debug.Assert(CheckFaceIntegrity(hNewFace));
        }

        Debug.Assert(CheckFaceIntegrity(hFace));

        hOutNewEdge = GetFullEdgeForHalfEdge(hNewEdgeAB);

        return hOutNewEdge.IsValid;
    }

    /// <summary>
    /// Collapses a face into a single vertex by collapsing its edges one after another.
    /// </summary>
    public bool CollapseFace(FaceHandle hFace, out VertexHandle hOutNewVertex)
    {
        hOutNewVertex = VertexHandle.Invalid;

        if (!hFace.IsValid)
            return false;

        int nNumFaceEdges = ComputeNumEdgesInFace(hFace);
        if (nNumFaceEdges <= 0)
            return false;

        // Build a list of all of the edges in the face
        var vertexList = new VertexHandle[nNumFaceEdges];
        int nVertexCount = 0;
        var hEdge = hFace.Edge;
        do
        {
            vertexList[nVertexCount++] = hEdge.Vertex;
            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);
        Debug.Assert(nVertexCount == nNumFaceEdges);

        // Collapse all of the edges. Note that collapsing one edge may remove others
        // in the list and eventually the face itself will be removed by this process.
        var hCollapsedFaceVertex = VertexHandle.Invalid;
        var hCurrentVertex = vertexList[0];
        for (int iVertex = 1; iVertex < nNumFaceEdges; ++iVertex)
        {
            var hFullEdge = FindFullEdgeConnectingVertices(hCurrentVertex, vertexList[iVertex]);
            if (hFullEdge.IsValid)
            {
                CollapseEdge(hFullEdge, out hCurrentVertex, out var _);

                if (!hCurrentVertex.IsValid)
                    break;
            }

            hCollapsedFaceVertex = hCurrentVertex;
        }

        hOutNewVertex = hCollapsedFaceVertex;

        return hCollapsedFaceVertex.IsValid;
    }

    /// <summary>
    /// Collapses an edge into a single vertex, merging the edges that end up overlapping.
    /// </summary>
    public bool CollapseEdge(HalfEdgeHandle hFullEdge, out VertexHandle pOutNewVertex, out List<(HalfEdgeHandle, HalfEdgeHandle)>? pOutEdgeReplacements)
    {
        return CollapseEdge(hFullEdge, out pOutNewVertex, false, out pOutEdgeReplacements);
    }

    /// <summary>
    /// Collapses an edge into a single vertex, merging the edges that end up overlapping. With check only nothing is changed, only whether the collapse is possible is reported.
    /// </summary>
    public bool CollapseEdge(HalfEdgeHandle hFullEdge, out VertexHandle pOutNewVertex, bool bCheckOnly, out List<(HalfEdgeHandle, HalfEdgeHandle)>? pOutEdgeReplacements)
    {
        pOutNewVertex = VertexHandle.Invalid;
        pOutEdgeReplacements = null;

        if (!hFullEdge.IsValid)
            return false;

        GetVerticesConnectedToHalfEdge(hFullEdge, out var hVertexA, out var hVertexB);
        var hEdgeA = hFullEdge;
        var hEdgeB = hFullEdge.OppositeEdge;
        var hFaceA = hEdgeA.Face;
        var hFaceB = hEdgeB.Face;

        // Find the pairs of edges which will be overlapping once the specified edge is collapsed.
        var overlappingEdgeA1 = HalfEdgeHandle.Invalid;
        var overlappingEdgeA2 = HalfEdgeHandle.Invalid;
        {
            var pEdgeA = hEdgeA;
            var pNextEdge = pEdgeA.NextEdge;
            if (pNextEdge.NextEdge.NextEdge == hEdgeA)
            {
                overlappingEdgeA1 = pEdgeA.NextEdge;
                overlappingEdgeA2 = pNextEdge.NextEdge;
            }
        }

        var overlappingEdgeB1 = HalfEdgeHandle.Invalid;
        var overlappingEdgeB2 = HalfEdgeHandle.Invalid;
        {
            var pEdgeB = hEdgeB;
            var pNextEdge = pEdgeB.NextEdge;
            if (pNextEdge.NextEdge.NextEdge == hEdgeB)
            {
                overlappingEdgeB1 = pEdgeB.NextEdge;
                overlappingEdgeB2 = pNextEdge.NextEdge;
            }
        }

        // Check to see if there are any edges that would be overlapping once the specified edge is collapsed 
        // that are not attached to same face as one of the edges, in this case the edge cannot be collapsed.
        var hStartEdge = hVertexA.Edge;
        var hCurrentEdge = hStartEdge;
        do
        {
            var hEdgeAToN = hCurrentEdge;
            var pEdgeAToN = hEdgeAToN;
            hCurrentEdge = pEdgeAToN.OppositeEdge.NextEdge;

            var hVertexN = pEdgeAToN.Vertex;
            var hEdgeNToB = FindHalfEdgeConnectingVertices(hVertexN, hVertexB);
            if (hEdgeNToB.IsValid)
            {
                var pEdgeNToB = hEdgeNToB;
                var hEdgeNToA = pEdgeAToN.OppositeEdge;
                var hEdgeBToN = pEdgeNToB.OppositeEdge;
                var pEdgeNToA = hEdgeNToA;
                var pEdgeBToN = hEdgeBToN;

                // If the edge pair is one of the already found overlapping 
                // edge pairs there is no need to test the face, it is allowed.
                if (((hEdgeAToN == overlappingEdgeA1) && (hEdgeNToB == overlappingEdgeA2)) ||
                     ((hEdgeAToN == overlappingEdgeA2) && (hEdgeNToB == overlappingEdgeA1)))
                    continue;

                if (((hEdgeAToN == overlappingEdgeB1) && (hEdgeNToB == overlappingEdgeB2)) ||
                     ((hEdgeAToN == overlappingEdgeB2) && (hEdgeNToB == overlappingEdgeB1)))
                    continue;

                if (((hEdgeBToN == overlappingEdgeA1) && (hEdgeNToA == overlappingEdgeA2)) ||
                     ((hEdgeBToN == overlappingEdgeA2) && (hEdgeNToA == overlappingEdgeA1)))
                    continue;

                if (((hEdgeBToN == overlappingEdgeB1) && (hEdgeNToA == overlappingEdgeB2)) ||
                     ((hEdgeBToN == overlappingEdgeB2) && (hEdgeNToA == overlappingEdgeB1)))
                    continue;

                if ((pEdgeAToN.Face == pEdgeNToB.Face) && (pEdgeAToN.Face != FaceHandle.Invalid))
                {
                    if ((pEdgeAToN.Face == hFaceA) && ((hEdgeAToN == overlappingEdgeA1) || (hEdgeAToN == overlappingEdgeA2)))
                        continue;

                    if ((pEdgeAToN.Face == hFaceB) && ((hEdgeAToN == overlappingEdgeB1) || (hEdgeAToN == overlappingEdgeB2)))
                        continue;
                }

                if ((pEdgeBToN.Face == pEdgeNToA.Face) && (pEdgeBToN.Face != FaceHandle.Invalid))
                {
                    if ((pEdgeBToN.Face == hFaceA) && ((hEdgeBToN == overlappingEdgeA1) || (hEdgeBToN == overlappingEdgeA2)))
                        continue;

                    if ((pEdgeBToN.Face == hFaceB) && ((hEdgeBToN == overlappingEdgeB1) || (hEdgeBToN == overlappingEdgeB2)))
                        continue;
                }

                // Neither the edge path connecting vertex a to b or the path connecting vertex b to a 
                // were connected to either of the faces directly connected to the edge being collapsed.
                // This means collapsing the edge could result in bad topology, the collapse is not allowed.
                return false;
            }
        }
        while (hCurrentEdge != hStartEdge);

        if (bCheckOnly)
            return true;

        // Create the new vertex and point all the edges that were terminating 
        // at either of the old vertices to the new vertex.
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (!hNewVertex.IsValid)
            return false;

        RedirectEdgesToVertex(hVertexA, hNewVertex);
        RedirectEdgesToVertex(hVertexB, hNewVertex);

        // Disconnect the edge that is being collapsed from the faces and other edges.
        Debug.Assert(hEdgeA.IsValid && hEdgeB.IsValid);
        if (hEdgeA.IsValid && hEdgeB.IsValid)
        {
            var pNewVertex = hNewVertex;
            var hNextEdgeA = hEdgeA.NextEdge;
            var hPrevEdgeA = FindPreviousEdgeInFaceLoop(hEdgeA);
            var hNextEdgeB = hEdgeB.NextEdge;
            var hPrevEdgeB = FindPreviousEdgeInFaceLoop(hEdgeB);

            hPrevEdgeB.NextEdge = hNextEdgeB;
            hPrevEdgeA.NextEdge = hNextEdgeA;

            var pFaceA = hFaceA;
            if (pFaceA.IsValid)
                pFaceA.Edge = hNextEdgeA;

            var pFaceB = hFaceB;
            if (pFaceB.IsValid)
                pFaceB.Edge = hNextEdgeB;

            // Make sure the new vertex is not referencing the edge being collapsed
            if ((pNewVertex.Edge == hEdgeA) || (pNewVertex.Edge == hEdgeB))
            {
                pNewVertex.Edge = hNextEdgeA;
            }
            Debug.Assert((pNewVertex.Edge != hEdgeA) && (pNewVertex.Edge != hEdgeB));

            // Remove the old vertices
            hVertexA.Edge = HalfEdgeHandle.Invalid;
            RemoveVertex(hVertexA, false);
            hVertexB.Edge = HalfEdgeHandle.Invalid;
            RemoveVertex(hVertexB, false);

            // Remove the old edge
            hEdgeA.Face = FaceHandle.Invalid;
            hEdgeB.Face = FaceHandle.Invalid;
            hEdgeA.Vertex = VertexHandle.Invalid;
            hEdgeB.Vertex = VertexHandle.Invalid;
            RemoveHalfEdgePair(hEdgeA, false);
        }

        pOutEdgeReplacements = new();

        // Merge the edges that are now overlapping and remove the faces which have become 2-sided
        if (MergeOverlappingEdges(overlappingEdgeA1, overlappingEdgeA2, out var mergedEdgeA))
        {
            pOutEdgeReplacements.Add((overlappingEdgeA1, mergedEdgeA));
            pOutEdgeReplacements.Add((overlappingEdgeA2, mergedEdgeA));
        }

        if (MergeOverlappingEdges(overlappingEdgeB1, overlappingEdgeB2, out var mergedEdgeB))
        {
            pOutEdgeReplacements.Add((overlappingEdgeB1, mergedEdgeB));
            pOutEdgeReplacements.Add((overlappingEdgeB2, mergedEdgeB));
        }

        Debug.Assert(CheckVertexEdgeIntegrity(hNewVertex));

        // Remove any loose edges that were created as a result of the the edge collapse. This can
        // occur if an edge on an interior edge loop is collapsed, removing the interior face loop 
        // leaving just a series of loose interior edges.
        RemoveLooseEdgesInFace(hFaceA);
        RemoveLooseEdgesInFace(hFaceB);

        Debug.Assert(CheckVertexEdgeIntegrity(hNewVertex));
        Debug.Assert(!hFaceA.IsValid || CheckFaceIntegrity(hFaceA));
        Debug.Assert(!hFaceB.IsValid || CheckFaceIntegrity(hFaceB));

        // If the edge that was collapsed was on a triangular face which was removed as a result of the
        // edges collapse it is possible the new vertex was actually removed if the edge was not shared
        // with any other faces.
        if (!hNewVertex.IsValid)
        {
            hNewVertex = VertexHandle.Invalid;
        }

        pOutNewVertex = hNewVertex;

        return true;
    }

    private static bool CheckVertexEdgeIntegrity(VertexHandle hVertex, bool bAssert = true)
    {
        var hStartEdge = GetFirstEdgeInVertexLoop(hVertex);
        if (!hStartEdge.IsValid)
            return true;

        var hCurrentEdge = hStartEdge;
        do
        {
            if (!CheckEdgeIntegrity(hCurrentEdge, bAssert))
                return false;

            hCurrentEdge = GetNextEdgeInVertexLoop(hCurrentEdge);
        }
        while (hCurrentEdge != hStartEdge);

        return true;
    }

    private bool MergeOverlappingEdges(HalfEdgeHandle hHalfEdgeA, HalfEdgeHandle hHalfEdgeB, out HalfEdgeHandle pOutNewEdge)
    {
        pOutNewEdge = HalfEdgeHandle.Invalid;

        if (!hHalfEdgeA.IsValid || !hHalfEdgeB.IsValid)
            return false;

        // Both edges must refer to each other as the next edge
        Debug.Assert(hHalfEdgeA.NextEdge == hHalfEdgeB);
        Debug.Assert(hHalfEdgeB.NextEdge == hHalfEdgeA);
        if ((hHalfEdgeA.NextEdge != hHalfEdgeB) || (hHalfEdgeB.NextEdge != hHalfEdgeA))
            return false;

        // Both edges must refer to the same face
        Debug.Assert(hHalfEdgeA.Face == hHalfEdgeB.Face);
        if (hHalfEdgeA.Face != hHalfEdgeB.Face)
            return false;

        // The two half edges must be opposites, but not each others opposites
        var hOppositeEdgeA = hHalfEdgeA.OppositeEdge;
        var hOppositeEdgeB = hHalfEdgeB.OppositeEdge;
        Debug.Assert(hOppositeEdgeA.Vertex == hHalfEdgeB.Vertex);
        Debug.Assert(hOppositeEdgeB.Vertex == hHalfEdgeA.Vertex);
        Debug.Assert(hOppositeEdgeA != hOppositeEdgeB);
        if ((hOppositeEdgeA.Vertex != hHalfEdgeB.Vertex) ||
             (hOppositeEdgeB.Vertex != hHalfEdgeA.Vertex) ||
             (hOppositeEdgeA == hOppositeEdgeB))
            return false;

        // Remove the shared face
        if (hHalfEdgeA.Face != FaceHandle.Invalid)
        {
            var hFace = hHalfEdgeA.Face;
            DetachFaceFromEdges(hFace);
            RemoveFace(hFace, false);
        }

        // Both edge should now be open
        Debug.Assert(hHalfEdgeA.Face == FaceHandle.Invalid);
        Debug.Assert(hHalfEdgeB.Face == FaceHandle.Invalid);

        // Create a new half edge pair which will be a connected pair of the 
        // opposite edges of the open edges which are being connected.
        if (!AllocateHalfEdgePair(out var hNewHalfEdgeA, out var hNewHalfEdgeB, hOppositeEdgeA.Index, hOppositeEdgeB.Index))
            return false;

        {
            hNewHalfEdgeA.NextEdge = hOppositeEdgeA.NextEdge;
            hNewHalfEdgeA.Face = hOppositeEdgeA.Face;
            hNewHalfEdgeA.Vertex = hOppositeEdgeA.Vertex;

            var hPrevEdgeA = FindPreviousEdgeInFaceLoop(hOppositeEdgeA);
            Debug.Assert(hPrevEdgeA.NextEdge == hOppositeEdgeA);
            hPrevEdgeA.NextEdge = hNewHalfEdgeA;

            var hVertA = hNewHalfEdgeA.Vertex;
            hVertA.Edge = hNewHalfEdgeB;
            if (hNewHalfEdgeA.Face != FaceHandle.Invalid)
            {
                if (hNewHalfEdgeA.Face.Edge == hOppositeEdgeA)
                {
                    var hFaceA = hNewHalfEdgeA.Face;
                    hFaceA.Edge = hNewHalfEdgeA;
                }
            }

            hNewHalfEdgeB.NextEdge = hOppositeEdgeB.NextEdge;
            hNewHalfEdgeB.Face = hOppositeEdgeB.Face;
            hNewHalfEdgeB.Vertex = hOppositeEdgeB.Vertex;

            var hPrevEdgeB = FindPreviousEdgeInFaceLoop(hOppositeEdgeB);
            Debug.Assert(hPrevEdgeB.NextEdge == hOppositeEdgeB);
            hPrevEdgeB.NextEdge = hNewHalfEdgeB;

            var hVertB = hNewHalfEdgeB.Vertex;
            hVertB.Edge = hNewHalfEdgeA;
            if (hNewHalfEdgeB.Face != FaceHandle.Invalid)
            {
                if (hNewHalfEdgeB.Face.Edge == hOppositeEdgeB)
                {
                    var hFaceB = hNewHalfEdgeB.Face;
                    hFaceB.Edge = hNewHalfEdgeB;
                }
            }
        }

        // Remove the old half edge pairs
        FreeHalfEdgePair(hHalfEdgeA);
        FreeHalfEdgePair(hHalfEdgeB);

        // If the resulting edge has no connected faces or is a loose edge remove it
        if (((hNewHalfEdgeA.Face == FaceHandle.Invalid) && (hNewHalfEdgeB.Face == FaceHandle.Invalid)) ||
             (hNewHalfEdgeA.NextEdge == hNewHalfEdgeB) || (hNewHalfEdgeB.NextEdge == hNewHalfEdgeA))
        {
            RemoveHalfEdgePair(hNewHalfEdgeA, true);
        }
        else
        {
            pOutNewEdge = hNewHalfEdgeA;
        }

        Debug.Assert(!IsHalfEdgeInMesh(hNewHalfEdgeA) || CheckEdgeIntegrity(hNewHalfEdgeA));
        Debug.Assert(!IsHalfEdgeInMesh(hNewHalfEdgeB) || CheckEdgeIntegrity(hNewHalfEdgeB));
        Debug.Assert(!IsHalfEdgeInMesh(hNewHalfEdgeA) || (hNewHalfEdgeA.Face == FaceHandle.Invalid) || CheckFaceIntegrity(hNewHalfEdgeA.Face));
        Debug.Assert(!IsHalfEdgeInMesh(hNewHalfEdgeB) || (hNewHalfEdgeB.Face == FaceHandle.Invalid) || CheckFaceIntegrity(hNewHalfEdgeB.Face));

        return true;
    }

    private static bool CheckEdgeIntegrity(HalfEdgeHandle hEdge, bool bAssert = true)
    {
        Debug.Assert(hEdge.IsValid || (bAssert == false));
        if (!hEdge.IsValid)
            return false;

        // 1. Every half edge must be matched with a corresponding opposite half edge to form a pair.
        var hOppositeEdge = GetOppositeHalfEdge(hEdge);
        Debug.Assert(hOppositeEdge.IsValid || (bAssert == false));
        if (!hOppositeEdge.IsValid)
            return false;

        Debug.Assert((hOppositeEdge.OppositeEdge == hEdge) || (bAssert == false));
        if (hOppositeEdge.OppositeEdge != hEdge)
            return false;

        GetVerticesConnectedToHalfEdge(hEdge, out var hVertexA, out var hVertexB);
        GetVerticesConnectedToHalfEdge(hEdge.OppositeEdge, out var hAdjVertexA, out var hAdjVertexB);
        Debug.Assert((hVertexA == hAdjVertexB) || (bAssert == false));
        if (hVertexA != hAdjVertexB)
            return false;

        Debug.Assert((hVertexB == hAdjVertexA) || (bAssert == false));
        if (hVertexB != hAdjVertexA)
            return false;

        Debug.Assert((hVertexA != hVertexB) || (bAssert == false));
        if (hVertexA == hVertexB)
            return false;

        // 2. Each half edge pair must refer to at least one face.
        Debug.Assert((hEdge.Face != FaceHandle.Invalid) || (hOppositeEdge.Face != FaceHandle.Invalid) || (bAssert == false));
        if ((hEdge.Face == FaceHandle.Invalid) && (hOppositeEdge.Face == FaceHandle.Invalid))
            return false;

        // If the half edge refers to a face it must be valid and must refer back to the edge
        if (hEdge.Face != FaceHandle.Invalid)
        {
            // All valid handles within the mesh should always correspond to valid components
            Debug.Assert(hEdge.Face.IsValid || (bAssert == false));
            var hFace = hEdge.Face;
            if (!hFace.IsValid)
                return false;

            // An edge must be in the edge loop of the face it is connected to
            var hStartEdge = hFace.Edge;
            var hCurrentEdge = hStartEdge;
            while (hCurrentEdge != hEdge)
            {
                hCurrentEdge = hCurrentEdge.NextEdge;

                // Traversed the whole face edge loop and did not find the edge
                Debug.Assert((hCurrentEdge != hStartEdge) || (bAssert == false));
                if (hCurrentEdge == hStartEdge)
                    return false;
            }
        }

        // 3. The next edge reference of an edge must always be valid.
        Debug.Assert((hEdge.NextEdge != HalfEdgeHandle.Invalid) || (bAssert == false));
        if (hEdge.NextEdge == HalfEdgeHandle.Invalid)
            return false;

        var hNextEdge = hEdge.NextEdge;
        Debug.Assert(hNextEdge.IsValid || (bAssert == false));
        if (!hNextEdge.IsValid)
            return false;

        // 4. The edge specified by the next edge reference must refer to the same face as this edge.
        Debug.Assert((hEdge.Face == hNextEdge.Face) || (bAssert == false));
        if (hEdge.Face != hNextEdge.Face)
            return false;

        // 5. An edge may not refer to its opposite edge as it next edge.
        Debug.Assert((hNextEdge != hOppositeEdge) || (bAssert == false));
        if (hNextEdge == hOppositeEdge)
            return false;

        // 6. The vertex reference of and edge must always be valid
        Debug.Assert((hEdge.Vertex != VertexHandle.Invalid) || (bAssert == false));
        if (hEdge.Vertex == VertexHandle.Invalid)
            return false;

        var hVertex = hEdge.Vertex;
        Debug.Assert(hVertex.IsValid || (bAssert == false));
        if (!hVertex.IsValid)
            return false;

        // 7. Both half edges of a pair may not specify the same vertex
        Debug.Assert((hEdge.Vertex != hOppositeEdge.Vertex) || (bAssert == false));
        if (hEdge.Vertex == hOppositeEdge.Vertex)
            return false;

        // 8. An edge's opposite edge must originate from the end vertex specified by the edge and
        // therefore must be in the edge loop around the vertex. 
        Debug.Assert((hVertex.Edge != HalfEdgeHandle.Invalid) || (bAssert == false));
        if (hVertex.Edge == HalfEdgeHandle.Invalid)
            return false;

        bool bFoundOpposite = false;
        {
            var hCurrentEdge = hVertex.Edge;
            do
            {
                if (hCurrentEdge == hEdge.OppositeEdge)
                {
                    bFoundOpposite = true;
                    break;
                }
                hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
            }
            while (hCurrentEdge != hVertex.Edge);
        }

        Debug.Assert((bFoundOpposite) || (bAssert == false));
        if (bFoundOpposite == false)
            return false;

        // 9. There may never be two edges which start and end at the same vertex. 
        var hOverlappingEdge = FindOverlappingEdge(hEdge);
        Debug.Assert(!hOverlappingEdge.IsValid || (bAssert == false));
        if (hOverlappingEdge.IsValid)
            return false;

        return true;
    }

    private static HalfEdgeHandle FindOverlappingEdge(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
            return HalfEdgeHandle.Invalid;

        // Test all of the edges originating the at start vertex of
        // this edge and check to see if they end at the same vertex.
        var hVertex = hHalfEdge.OppositeEdge.Vertex;
        var hCurrentEdge = hVertex.Edge;
        do
        {
            if (hCurrentEdge != hHalfEdge)
            {
                if (hCurrentEdge.Vertex == hHalfEdge.Vertex)
                    return hCurrentEdge;
            }
            hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
        }
        while (hCurrentEdge != hVertex.Edge);

        return HalfEdgeHandle.Invalid;
    }

    private static void DetachFaceFromEdges(FaceHandle hFace)
    {
        if (!hFace.IsValid)
            return;

        if (hFace.Edge != HalfEdgeHandle.Invalid)
        {
            var hCurrentEdge = hFace.Edge;
            do
            {
                hCurrentEdge.Face = FaceHandle.Invalid;
                hCurrentEdge = hCurrentEdge.NextEdge;
            }
            while (hCurrentEdge != hFace.Edge);
        }

        hFace.Edge = HalfEdgeHandle.Invalid;
    }

    private static void RedirectEdgesToVertex(VertexHandle hOldVertex, VertexHandle hNewVertex)
    {
        if (!hNewVertex.IsValid)
            return;

        // Redirect all of the edges ending at the old vertex to end at the new vertex
        var hStartEdge = hOldVertex.Edge;
        var hCurrentEdge = hStartEdge;
        do
        {
            var hOppositeEdge = hCurrentEdge.OppositeEdge;
            Debug.Assert(hOppositeEdge.Vertex == hOldVertex);

            hOppositeEdge.Vertex = hNewVertex;
            hNewVertex.Edge = hCurrentEdge;

            hCurrentEdge = hOppositeEdge.NextEdge;
        }
        while (hCurrentEdge != hStartEdge);
    }

    /// <summary>
    /// Finds the two pairs of vertices merging two open edges would join.
    /// </summary>
    public static bool GetEdgeMergeVertexPairs(HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB,
        out VertexHandle vertexPairA1, out VertexHandle vertexPairA2,
        out VertexHandle vertexPairB1, out VertexHandle vertexPairB2)
    {
        vertexPairA1 = VertexHandle.Invalid;
        vertexPairA2 = VertexHandle.Invalid;
        vertexPairB1 = VertexHandle.Invalid;
        vertexPairB2 = VertexHandle.Invalid;

        // Get the open half edge of each edge, both edges must have one open half edge.
        var hOpenHalfEdgeA = GetOpenHalfEdgeFromFullEdge(hEdgeA);
        var hOpenHalfEdgeB = GetOpenHalfEdgeFromFullEdge(hEdgeB);
        if ((hOpenHalfEdgeA == HalfEdgeHandle.Invalid) || (hOpenHalfEdgeB == HalfEdgeHandle.Invalid))
            return false;

        vertexPairA1 = hOpenHalfEdgeA.Vertex;
        vertexPairA2 = hOpenHalfEdgeB.OppositeEdge.Vertex;

        vertexPairB1 = hOpenHalfEdgeA.OppositeEdge.Vertex;
        vertexPairB2 = hOpenHalfEdgeB.Vertex;

        return true;
    }

    /// <summary>
    /// Merges two open edges into one by merging their end vertices pairwise.
    /// </summary>
    public bool MergeEdges(HalfEdgeHandle hEdgeA, HalfEdgeHandle hEdgeB, out VertexHandle hOutNewVertexA, out VertexHandle hOutNewVertexB)
    {
        hOutNewVertexA = VertexHandle.Invalid;
        hOutNewVertexB = VertexHandle.Invalid;

        // Get the open half edge of each edge, both edges must have one open half edge.
        var hOpenHalfEdgeA = GetOpenHalfEdgeFromFullEdge(hEdgeA);
        var hOpenHalfEdgeB = GetOpenHalfEdgeFromFullEdge(hEdgeB);
        if ((hOpenHalfEdgeA == HalfEdgeHandle.Invalid) || (hOpenHalfEdgeB == HalfEdgeHandle.Invalid))
            return false;

        // The opposite edges of the open half edges must not belong to the same face
        if (hOpenHalfEdgeA.OppositeEdge.Face == hOpenHalfEdgeB.OppositeEdge.Face)
            return false;

        // Two edges which start or end at the same vertex may not be merged
        if (hOpenHalfEdgeA.Vertex == hOpenHalfEdgeB.Vertex)
            return false;

        if (hOpenHalfEdgeA.OppositeEdge.Vertex == hOpenHalfEdgeB.OppositeEdge.Vertex)
            return false;

        // Build the pairs of vertices that will need to be merged.
        if (!GetEdgeMergeVertexPairs(hEdgeA, hEdgeB, out var vertexPairA1, out var vertexPairA2, out var vertexPairB1, out var vertexPairB2))
            return false;

        // If either of the vertices are shared already, just merge the other pair of vertices.
        if (vertexPairA1 == vertexPairA2)
        {
            hOutNewVertexA = vertexPairA1;
            return MergeVertices(vertexPairB1, vertexPairB2, out hOutNewVertexB);
        }

        if (vertexPairB1 == vertexPairB2)
        {
            hOutNewVertexB = vertexPairB1;
            return MergeVertices(vertexPairA1, vertexPairA2, out hOutNewVertexA);
        }

        // Test to see if both pairs of vertices can be merged. Performing this check helps avoid the 
        // case where merging the edge results in merging a single vertex of the edge but not both.
        if ((!MergeVertices(vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out _, true)) ||
             (!MergeVertices(vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out _, true)))
            return false;

        // If both pairs can be merged then merge them.
        if (!MergeVertices(vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out hOutNewVertexA, false))
            return false;

        if (!MergeVertices(vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out hOutNewVertexB, false))
            return false;

        return true;
    }

    /// <summary>
    /// Merges two vertices into one where the topology allows it: connected vertices collapse their edge, otherwise the vertices must each have one open edge.
    /// </summary>
    public bool MergeVertices(VertexHandle hVertexA, VertexHandle hVertexB, out VertexHandle hOutNewVertex)
    {
        return MergeVertices(hVertexA, hVertexB, HalfEdgeHandle.Invalid, HalfEdgeHandle.Invalid, out hOutNewVertex, false);
    }

    /// <summary>
    /// Merges two vertices into one along the given open edges. With check only nothing is changed, only whether the merge is possible is reported.
    /// </summary>
    public bool MergeVertices(VertexHandle hVertexA, VertexHandle hVertexB, HalfEdgeHandle hOpenEdgeA, HalfEdgeHandle hOpenEdgeB, out VertexHandle hOutNewVertex, bool bCheckOnly)
    {
        hOutNewVertex = VertexHandle.Invalid;

        // If the two specified vertices are actually the same vertex, do nothing but return true
        if (hVertexA == hVertexB)
        {
            hOutNewVertex = hVertexA;
            return true;
        }

        // First check to see if there is an edge connecting the two vertices, if so collapse the edge
        var hFullEdge = FindFullEdgeConnectingVertices(hVertexA, hVertexB);
        if (hFullEdge != HalfEdgeHandle.Invalid)
        {
            return CollapseEdge(hFullEdge, out hOutNewVertex, bCheckOnly, out var _);
        }

        // If an open edge was not specified to use in merging the vertices, check to see if there is 
        // exactly one open edge starting at the vertex, if so use that one, otherwise the vertices may 
        // not be merged.
        if (hOpenEdgeA != HalfEdgeHandle.Invalid)
        {
            Debug.Assert(hOpenEdgeA.Face == FaceHandle.Invalid);
            Debug.Assert(hOpenEdgeA.OppositeEdge.Vertex == hVertexA);
        }
        else if (ComputeNumOpenEdgesInVertexLoop(hVertexA) == 1)
        {
            hOpenEdgeA = FindFirstOpenEdgeInVertexLoop(hVertexA);
        }

        if (hOpenEdgeB != HalfEdgeHandle.Invalid)
        {
            Debug.Assert(hOpenEdgeB.Face == FaceHandle.Invalid);
            Debug.Assert(hOpenEdgeB.OppositeEdge.Vertex == hVertexB);
        }
        else if (ComputeNumOpenEdgesInVertexLoop(hVertexB) == 1)
        {
            hOpenEdgeB = FindFirstOpenEdgeInVertexLoop(hVertexB);
        }

        if ((hOpenEdgeA == HalfEdgeHandle.Invalid) || (hOpenEdgeB == HalfEdgeHandle.Invalid))
            return false;

        // Now check to see if there is a pair of open edges connecting the two vertices. If so create
        // a triangle face and use the collapse edge function to collapse the new edge resulting in 
        // merging the vertices.
        {
            // Now see if there is an open edge connecting the vertex
            // at the end of the open edge (vertex N) to vertex B.
            var hVertexN = hOpenEdgeA.Vertex;
            var hEdgeNToB = FindHalfEdgeConnectingVertices(hVertexN, hVertexB);
            if (hEdgeNToB != HalfEdgeHandle.Invalid)
            {
                // If there is an edge but it is not open the vertices cannot be merged
                if (hEdgeNToB.Face != FaceHandle.Invalid)
                    return false;

                if (!AddFace(out var hNewFace, hVertexA, hVertexN, hVertexB))
                    return false;

                hFullEdge = FindFullEdgeConnectingVertices(hVertexA, hVertexB);
                bool bSuccess = CollapseEdge(hFullEdge, out hOutNewVertex, bCheckOnly, out var _);
                // edit from s&box code, must also remove temporary triangle in case of failure,
                // otherwise the mesh gets polluted by hard to find ghost triangles
                if (bCheckOnly || !bSuccess)
                {
                    RemoveFace(hNewFace, false);
                }
                return bSuccess;
            }
        }

        // If creating a face using the open edge from A to N failed try the open edge from B to M.
        {
            var hVertexM = hOpenEdgeB.Vertex;
            var hEdgeMToA = FindHalfEdgeConnectingVertices(hVertexM, hVertexA);
            if (hEdgeMToA != HalfEdgeHandle.Invalid)
            {
                if (hEdgeMToA.Face != FaceHandle.Invalid)
                    return false;

                if (!AddFace(out var hNewFace, hVertexB, hVertexM, hVertexA))
                    return false;

                hFullEdge = FindFullEdgeConnectingVertices(hVertexA, hVertexB);
                var bSuccess = CollapseEdge(hFullEdge, out hOutNewVertex, bCheckOnly, out var _);
                // edit from s&box code, must also remove temporary triangle in case of failure,
                // otherwise the mesh gets polluted by hard to find ghost triangles
                if (bCheckOnly || !bSuccess)
                {
                    RemoveFace(hNewFace, false);
                }
                return bSuccess;
            }
        }

        // If we have reached this point the vertices do not have a single edge or a pair of open edges
        // that connect them. They may be merged as long as they do not belong to the same face and there
        // is not a pair of edges connecting them.
        var hClosedEdgeA = hOpenEdgeA.OppositeEdge;
        var hClosedEdgeB = hOpenEdgeB.OppositeEdge;
        if (hClosedEdgeA.Face == hClosedEdgeB.Face)
            return false;

        if (AreVerticesConnectedByEdgePair(hVertexA, hVertexB))
            return false;

        // Find the previous edges to which refer to the open edges as their next edge. 
        // Note these edge will be open as well.
        var hPreviousOpenEdgeA = FindPreviousEdgeInFaceLoop(hOpenEdgeA);
        var hPreviousOpenEdgeB = FindPreviousEdgeInFaceLoop(hOpenEdgeB);
        var pPreviousOpenEdgeA = hPreviousOpenEdgeA;
        var pPreviousOpenEdgeB = hPreviousOpenEdgeB;
        Debug.Assert(pPreviousOpenEdgeA.IsValid);
        Debug.Assert(pPreviousOpenEdgeB.IsValid);
        if (!pPreviousOpenEdgeA.IsValid || !pPreviousOpenEdgeB.IsValid)
            return false;

        if (bCheckOnly)
            return true;

        // If any of these conditions are not true there is a fundamental problem with
        // the topology or a bug in the FindPreviousEdgeInVertexLoop() function.
        Debug.Assert(pPreviousOpenEdgeA.Vertex == hVertexA);
        Debug.Assert(pPreviousOpenEdgeB.Vertex == hVertexB);
        Debug.Assert(pPreviousOpenEdgeA.NextEdge == hOpenEdgeA);
        Debug.Assert(pPreviousOpenEdgeB.NextEdge == hOpenEdgeB);
        Debug.Assert(pPreviousOpenEdgeA.Face == FaceHandle.Invalid);
        Debug.Assert(pPreviousOpenEdgeB.Face == FaceHandle.Invalid);

        // Create the new vertex and point all the edges that were terminating 
        // at either of the old vertices to the new vertex.
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (hNewVertex == VertexHandle.Invalid)
            return false;

        RedirectEdgesToVertex(hVertexA, hNewVertex);
        RedirectEdgesToVertex(hVertexB, hNewVertex);

        // Redirect the previous open edges at the open edge of the opposite vertex
        pPreviousOpenEdgeA.NextEdge = hOpenEdgeB;
        pPreviousOpenEdgeB.NextEdge = hOpenEdgeA;

        // Remove the old vertices
        hVertexA.Edge = HalfEdgeHandle.Invalid;
        hVertexB.Edge = HalfEdgeHandle.Invalid;
        RemoveVertex(hVertexA, false);
        RemoveVertex(hVertexB, false);

        Debug.Assert(CheckVertexEdgeIntegrity(hNewVertex));

        hOutNewVertex = hNewVertex;

        return true;
    }

    private static HalfEdgeHandle FindFirstOpenEdgeInVertexLoop(VertexHandle hVertex)
    {
        if (hVertex.IsValid)
        {
            var hEdge = hVertex.Edge;
            do
            {
                if (hEdge.Face == FaceHandle.Invalid)
                    return hEdge;

                hEdge = hEdge.OppositeEdge.NextEdge;
            }
            while (hEdge != hVertex.Edge);
        }

        return HalfEdgeHandle.Invalid;
    }

    private static bool AreVerticesConnectedByEdgePair(VertexHandle hVertexA, VertexHandle hVertexB)
    {
        var hStartEdge = GetFirstEdgeInVertexLoop(hVertexA);
        var hCurrentEdge = hStartEdge;

        do
        {
            if (FindHalfEdgeConnectingVertices(hCurrentEdge.Vertex, hVertexB) != HalfEdgeHandle.Invalid)
                return true;

            hCurrentEdge = GetNextEdgeInVertexLoop(hCurrentEdge);
        }
        while (hCurrentEdge != hStartEdge);
        return false;
    }

    /// <summary>
    /// Splits an edge by inserting a new vertex into it. The half edge keeps its start vertex and now ends at
    /// the new vertex, a new pair continues on to the old end vertex, copying the corner data of the old pair.
    /// </summary>
    public bool AddVertexToEdge(HalfEdgeHandle hHalfEdge, out VertexHandle hOutNewVertex)
    {
        hOutNewVertex = VertexHandle.Invalid;

        // Get one of the half edges of the full edge.
        var hExistingEdgeA = hHalfEdge;
        if (!hExistingEdgeA.IsValid)
            return false;

        GetVerticesConnectedToHalfEdge(hExistingEdgeA, out var hVertexA, out var hVertexB);

        var hExistingEdgeB = hExistingEdgeA.OppositeEdge;
        Debug.Assert(hExistingEdgeA.Vertex == hVertexB);
        Debug.Assert(hExistingEdgeB.Vertex == hVertexA);

        var hPrevEdgeB = FindPreviousEdgeInFaceLoop(hExistingEdgeB);
        Debug.Assert(hPrevEdgeB.IsValid);

        // Create the new edge pair, copying data streams from the existing edges
        // so that face-vertex attributes (colors, UVs, etc.) are preserved on the new segments.
        if (!AllocateHalfEdgePair(out var hNewEdgeA, out var hNewEdgeB, hExistingEdgeA.Index, hExistingEdgeB.Index))
            return false;

        // Create the new vertex
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (!hNewVertex.IsValid)
            return false;

        // Redirect the existing edge so that it
        // connects the new vertex with vertex A.
        hExistingEdgeA.Vertex = hNewVertex;

        // The new edge will connect the new vertex with vertex B
        hNewEdgeA.Vertex = hVertexB;
        hNewEdgeA.NextEdge = hExistingEdgeA.NextEdge;
        hNewEdgeA.Face = hExistingEdgeA.Face;
        hNewVertex.Edge = hNewEdgeA;

        hNewEdgeB.Vertex = hNewVertex;
        hNewEdgeB.NextEdge = hExistingEdgeB;
        hNewEdgeB.Face = hExistingEdgeB.Face;
        hVertexB.Edge = hNewEdgeB;

        hExistingEdgeA.NextEdge = hNewEdgeA;
        hPrevEdgeB.NextEdge = hNewEdgeB;

        hOutNewVertex = hNewVertex;

        return true;
    }

#pragma warning disable CA1043
    /// <summary>
    /// Gets the topology of a vertex, or <see cref="Vertex.Invalid"/> when the handle is not from this mesh.
    /// </summary>
    /// <param name="hVertex">Vertex to look up.</param>
    public Vertex this[VertexHandle hVertex]
    {
        get => hVertex.Mesh is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count ? VertexList[hVertex.Index] : Vertex.Invalid;
        private set
        {
            if (hVertex.Mesh is not null && hVertex.Index >= 0 && hVertex.Index < VertexList.Count)
                VertexList[hVertex.Index] = value;
        }
    }

    /// <summary>
    /// Gets the topology of a face, or <see cref="Face.Invalid"/> when the handle is not from this mesh.
    /// </summary>
    /// <param name="hFace">Face to look up.</param>
    public Face this[FaceHandle hFace]
    {
        get => hFace.Mesh is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count ? FaceList[hFace.Index] : Face.Invalid;
        private set
        {
            if (hFace.Mesh is not null && hFace.Index >= 0 && hFace.Index < FaceList.Count)
                FaceList[hFace.Index] = value;
        }
    }

    /// <summary>
    /// Gets the topology of a half edge, or <see cref="HalfEdge.Invalid"/> when the handle is not from this mesh.
    /// </summary>
    /// <param name="hEdge">Half edge to look up.</param>
    public HalfEdge this[HalfEdgeHandle hEdge]
    {
        get => hEdge.Mesh is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count ? HalfEdgeList[hEdge.Index] : HalfEdge.Invalid;
        private set
        {
            if (hEdge.Mesh is not null && hEdge.Index >= 0 && hEdge.Index < HalfEdgeList.Count)
                HalfEdgeList[hEdge.Index] = value;
        }
    }
#pragma warning restore CA1043
}


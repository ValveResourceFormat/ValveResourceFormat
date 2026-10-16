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

// Handles wrap raw integer indices into the topology lists (verts, half edges, faces) for safer access

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
    /// Called when corner data must follow a half edge, with the destination edge first and the source second.
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
        newVertices = [];
        newHalfEdges = [];
        newFaces = [];

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
            {
                hNewVertex.Edge = hEdge;
            }
        }

        foreach (var pair in newFaces)
        {
            var hFace = pair.Key;
            var hNewFace = pair.Value;

            if (newHalfEdges.TryGetValue(hFace.Edge, out var hEdge))
            {
                hNewFace.Edge = hEdge;
            }
        }

        foreach (var pair in newHalfEdges)
        {
            var hHalfEdge = pair.Key;
            var hNewHalfEdge = pair.Value;

            if (newVertices.TryGetValue(hHalfEdge.Vertex, out var hVertex))
            {
                hNewHalfEdge.Vertex = hVertex;
            }

            if (newHalfEdges.TryGetValue(hHalfEdge.OppositeEdge, out var hOppositeEdge))
            {
                hNewHalfEdge.OppositeEdge = hOppositeEdge;
            }

            if (newHalfEdges.TryGetValue(hHalfEdge.NextEdge, out var hNextEdge))
            {
                hNewHalfEdge.NextEdge = hNextEdge;
            }

            if (newFaces.TryGetValue(hHalfEdge.Face, out var hFace))
            {
                hNewHalfEdge.Face = hFace;
            }
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
        newVertices = [];
        newHalfEdges = [];
        newFaces = [];

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

        // A vertex where the copied faces only touch other faces (a bowtie) has edges outside the set.
        // The copy gets its own vertex, pointing at a copied edge, and its boundary loop closes along
        // the copied fan rather than leaving through the other fan.
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
        var vertexCount = VertexCount;
        VertexList.AllocateMultiple(count, Vertex.Invalid);

        for (var i = 0; i < count; i++)
        {
            yield return new(vertexCount + i, this);
        }
    }

    /// <summary>
    /// Adds a face bounded by the given vertices, in order.
    /// </summary>
    /// <param name="hVertices">Corner vertices of the new face.</param>
    /// <returns>The new face, or <see cref="FaceHandle.Invalid"/> when the face would break the mesh.</returns>
    public FaceHandle AddFace(params VertexHandle[] hVertices)
    {
        if (!AddFace(hVertices, out var hFace))
        {
            return FaceHandle.Invalid;
        }

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
        {
            return false;
        }

        return true;
    }

    /// <summary>
    /// Counts the edges around a vertex that border no face.
    /// </summary>
    /// <param name="hVertex">Vertex whose edge loop is walked.</param>
    public static int ComputeNumOpenEdgesInVertexLoop(VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
        {
            return 0;
        }

        var nNumOpenEdges = 0;

        // Count the edges emanating from the vertex that border no face
        var hEdge = hVertex.Edge;
        if (hVertex.Edge == HalfEdgeHandle.Invalid)
        {
            return 0;
        }

        do
        {
            if (hEdge.Face == FaceHandle.Invalid)
            {
                ++nNumOpenEdges;
            }

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
        {
            return HalfEdgeHandle.Invalid;
        }

        if (hVertex.Edge == HalfEdgeHandle.Invalid)
        {
            return HalfEdgeHandle.Invalid;
        }

        var hCurrentEdge = hVertex.Edge;

        do
        {
            var hOppositeEdge = GetOppositeHalfEdge(hCurrentEdge);
            if (hOppositeEdge.Face == FaceHandle.Invalid)
            {
                return hOppositeEdge;
            }

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
        {
            return HalfEdgeHandle.Invalid;
        }

        if (hVertex.Edge == HalfEdgeHandle.Invalid)
        {
            return HalfEdgeHandle.Invalid;
        }

        var hCurrentEdge = hVertex.Edge;

        do
        {
            var hOppositeEdge = GetOppositeHalfEdge(hCurrentEdge);
            if (hOppositeEdge.NextEdge == hNextEdge)
            {
                return hOppositeEdge;
            }

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
        {
            return HalfEdgeHandle.Invalid;
        }

        var hStartEdge = hEdge.NextEdge;
        var hCurrentEdge = hStartEdge;

        do
        {
            for (var iEdge = 0; iEdge < nNumEdges; ++iEdge)
            {
                if (hCurrentEdge == pEdges[iEdge])
                {
                    return hCurrentEdge;
                }
            }

            hCurrentEdge = GetNextEdgeInVertexLoop(hCurrentEdge);
        }
        while (hCurrentEdge != hStartEdge);

        return HalfEdgeHandle.Invalid;
    }

    private bool AllocateHalfEdgePair(out HalfEdgeHandle hHalfEdgeA, out HalfEdgeHandle hHalfEdgeB, int sourceIndexA = -1, int sourceIndexB = -1)
    {
        var halfEdgeCount = HalfEdgeCount;

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
        {
            return;
        }

        var hEdge = pAllEdges[nNumEdges - 1];
        for (var iEdge = 0; iEdge < nNumEdges; ++iEdge)
        {
            var hNextEdge = pAllEdges[iEdge];
            var hOppositeEdge = GetOppositeHalfEdge(hEdge);
            var hNextOppositeEdge = GetOppositeHalfEdge(hNextEdge);

            Debug.Assert(hNextOppositeEdge.Vertex == hEdge.Vertex);

            // Set the face first so this edge is skipped by the open edge search
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

            // A vertex's edge must start at the vertex, so assign the next edge if it has none
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

        // Point the face at the last edge so its vertex order matches the provided vertices
        hFace.Edge = pAllEdges[nNumEdges - 1];

        Debug.Assert(CheckFaceIntegrity(hFace));
    }

    private static bool CheckFaceIntegrity(FaceHandle hFace, bool bAssert = true)
    {
        Debug.Assert(hFace.IsValid || (!bAssert));
        if (!hFace.IsValid)
        {
            return false;
        }

        var hFirstEdge = hFace.Edge;
        Debug.Assert(hFirstEdge.IsValid || (!bAssert));
        if (!hFirstEdge.IsValid)
        {
            return false;
        }

        var hEdge = hFace.Edge;
        do
        {
            Debug.Assert(hEdge.IsValid || (!bAssert));
            if (!hEdge.IsValid)
            {
                return false;
            }

            Debug.Assert(hEdge.Face == hFace || (!bAssert));
            if (hEdge.Face != hFace)
            {
                return false;
            }

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
        {
            return false;
        }

        var pEdgeHandles = new HalfEdgeHandle[nNumVertices];
        var pVerticesB = new VertexHandle[nNumVertices];
        for (var iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            pVerticesB[iVertex] = pVerticesA[(iVertex + 1) % nNumVertices];
        }

        // Existing edges must be open and the new edges must be addable
        for (var iVertex = 0; iVertex < nNumVertices; ++iVertex)
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
                var nNumOpenEdges = ComputeNumOpenEdgesInVertexLoop(pVerticesB[iVertex]);

                // A new edge at a vertex with other edges needs an open edge to insert it after
                if (nNumOpenEdges == 0)
                {
                    return false;
                }

                // With two open edges, the next edge must already exist or the face is ambiguous
                if (nNumOpenEdges >= 2)
                {
                    if (!FindHalfEdgeConnectingVertices(pVerticesB[iVertex], pVerticesB[(iVertex + 1) % nNumVertices]).IsValid)
                    {
                        return false;
                    }
                }
            }
        }

        // Neighboring existing edges must be directly connected, with no edges between
        for (var iEdge = 0; iEdge < nNumVertices; ++iEdge)
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
        for (var iVertex = 0; iVertex < nNumVertices; ++iVertex)
        {
            if (!pEdgeHandles[iVertex].IsValid)
            {
                // An interior edge may already connect the vertices in the opposite direction
                for (var iEdge = 0; iEdge < iVertex; ++iEdge)
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

        AttachEdgesToFace(hFace, pEdgeHandles, nNumVertices);

        return true;
    }

    private void FreeHalfEdge(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
        {
            return;
        }

        this[hHalfEdge] = HalfEdge.Invalid;

        HalfEdgeList.Deallocate(hHalfEdge.Index);
    }

    private void FreeHalfEdgePair(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
        {
            return;
        }

        FreeHalfEdge(hHalfEdge.OppositeEdge);
        FreeHalfEdge(hHalfEdge);
    }

    private void FreeFace(FaceHandle hFace)
    {
        if (!hFace.IsValid)
        {
            return;
        }

        this[hFace] = Face.Invalid;
        FaceList.Deallocate(hFace.Index);
    }

    private void ClearEdgeData(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
        {
            return;
        }

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

    /// <summary>
    /// Merges the two faces sharing an edge. The face of the given half edge survives, the opposite face is
    /// freed together with the edge pair.
    /// </summary>
    /// <param name="hFullEdge">Edge to dissolve.</param>
    /// <param name="hOutFaceHandle">The surviving face, set only when this returns true.</param>
    /// <returns>Whether the edge was dissolved.</returns>
    public bool DissolveEdge(HalfEdgeHandle hFullEdge, out FaceHandle hOutFaceHandle)
    {
        hOutFaceHandle = FaceHandle.Invalid;

        if (!hFullEdge.IsValid)
        {
            return false;
        }

        var hAdjEdge = hFullEdge.OppositeEdge;
        var hFace = hFullEdge.Face;
        var hAdjFace = hAdjEdge.Face;

        // The edge must be connected to two different faces for dissolve to be a valid operation.
        if (!hFace.IsValid || !hAdjFace.IsValid || (hFace == hAdjFace))
        {
            return false;
        }

        // Move the opposite face's edges over to the current face
        var hAdjFaceEdge = hAdjEdge;
        do
        {
            hAdjFaceEdge.Face = hFace;
            hAdjFaceEdge = hAdjFaceEdge.NextEdge;
        }
        while (hAdjFaceEdge != hAdjEdge);

        // Always start the face at the edge after the removed one, so its first edge is consistent
        hFace.Edge = hFullEdge.NextEdge;

        // Detach the edge from the face to ensure
        // removing the edge does not destroy the face
        hFullEdge.Face = FaceHandle.Invalid;
        hAdjEdge.Face = FaceHandle.Invalid;

        // Remove the edge, then the other shared edges that became loose, so faces
        // sharing several edges merge too.
        RemoveEdge(hFullEdge, true);
        RemoveLooseEdgesInFace(hFace);

        // Clear the opposite face's edge first so removing it does not remove the moved edges
        hAdjFace.Edge = HalfEdgeHandle.Invalid;
        RemoveFace(hAdjFace, false);

        hOutFaceHandle = hFace;

        return true;
    }

    /// <summary>
    /// Returns whichever half of a full edge borders no face, or an invalid handle when both sides have a face.
    /// </summary>
    public static HalfEdgeHandle GetOpenHalfEdgeFromFullEdge(HalfEdgeHandle hEdge)
    {
        if (!hEdge.IsValid)
        {
            return HalfEdgeHandle.Invalid;
        }

        if (hEdge.Face == FaceHandle.Invalid)
        {
            return hEdge;
        }

        var hOpposite = hEdge.OppositeEdge;
        if (hOpposite.Face == FaceHandle.Invalid)
        {
            return hOpposite;
        }

        return HalfEdgeHandle.Invalid;
    }

    private void DetachEdgeFromVertex(HalfEdgeHandle hEdge, bool bRemoveFreeVerts)
    {
        if (!hEdge.IsValid)
        {
            return;
        }

        if (hEdge.Vertex == VertexHandle.Invalid)
        {
            return;
        }

        var hOppositeEdge = hEdge.OppositeEdge;
        var hVertex = hOppositeEdge.Vertex;

        // If other edges meet at the vertex, unlink this edge from its loop. Otherwise the vertex is
        // disconnected and removed when bRemoveFreeVerts is set.
        if (hOppositeEdge.NextEdge != hEdge)
        {
            var hPreviousEdge = FindPreviousEdgeInVertexLoop(hEdge);
            Debug.Assert(hPreviousEdge.OppositeEdge.NextEdge == hEdge);

            var hPrevOpp = hPreviousEdge.OppositeEdge;
            hPrevOpp.NextEdge = hOppositeEdge.NextEdge;

            // Stop the vertex referring to the detached edge
            hVertex.Edge = hOppositeEdge.NextEdge;

            hOppositeEdge.NextEdge = hEdge;
        }
        else
        {
            Debug.Assert(ComputeNumEdgesConnectedToVertex(hVertex) == 1);

            // If this is the only edge connected to the
            // vertex, the vertex should refer to it.
            Debug.Assert((hVertex.Edge == hEdge) || hVertex.Edge == HalfEdgeHandle.Invalid);

            hVertex.Edge = HalfEdgeHandle.Invalid;

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
        {
            return false;
        }

        var bValidEdge = hVertex.Edge != HalfEdgeHandle.Invalid;

        if (bValidEdge)
        {
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

            // Collect the incoming and outgoing edge pairs around the vertex, one per face
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

            // Removing the vertex would leave a triangle invalid, so remove the whole face
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
                    if (!ReplaceFaceEdges(pair.Face, pair.IncomingEdge, pair.OutgoingEdge, bRemoveFreeVerts))
                    {
                        RemoveFace(pair.Face, bRemoveFreeVerts);
                        pair.Face = FaceHandle.Invalid;
                        pFaceEdgePairs[iPair] = pair;
                    }
                }
            }
        }

        // A vertex with edges is already freed when bRemoveFreeVerts is set
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
        {
            return false;
        }

        // Both edges must belong to the face
        Debug.Assert((hIncomingEdge.Face == hFace) && (hOutgoingEdge.Face == hFace));
        if ((hIncomingEdge.Face != hFace) || (hOutgoingEdge.Face != hFace))
        {
            return false;
        }

        // The outgoing edge must be the next edge in the loop from the incoming edge.
        Debug.Assert(hIncomingEdge.NextEdge == hOutgoingEdge);
        if (hIncomingEdge.NextEdge != hOutgoingEdge)
        {
            return false;
        }

        // Count the number of edges the face has, it must have more than 3 edges
        var nFaceNumEdges = ComputeNumEdgesInFace(hFace);
        Debug.Assert(nFaceNumEdges > 3);
        if (nFaceNumEdges <= 3)
        {
            return false;
        }

        var hIncomingOppositeEdge = hIncomingEdge.OppositeEdge;

        // The new edge must connect two different valid vertices
        var hVertexA = hIncomingOppositeEdge.Vertex;
        var hVertexB = hOutgoingEdge.Vertex;
        Debug.Assert(hVertexA.IsValid && hVertexB.IsValid && (hVertexA != hVertexB));
        if (!hVertexA.IsValid || !hVertexB.IsValid || (hVertexA == hVertexB))
        {
            return false;
        }

        // Build a list of all of the edges in the face excluding the ones that are going to be removed.
        var pEdgeList = new HalfEdgeHandle[nFaceNumEdges];
        var nNumEdges = 0;
        var hCurrentEdge = hOutgoingEdge.NextEdge;
        do
        {
            pEdgeList[nNumEdges++] = hCurrentEdge;
            hCurrentEdge = hCurrentEdge.NextEdge;
        }
        while (hCurrentEdge != hIncomingEdge);
        Debug.Assert(nNumEdges == (nFaceNumEdges - 2));

        // A connecting edge may already exist when the edges form an open triangle loop, or when both
        // are internal to the face (both halves of a pair reference it) while replacing the second pair.
        var hConnectingEdge = FindHalfEdgeConnectingVertices(hVertexA, hVertexB);
        if (hConnectingEdge.IsValid)
        {
            // With faces on both edges this should be a triangle loop, where the incoming edge's
            // opposite leads to the connecting edge, which leads to the outgoing edge's opposite.
            // A face attached to a vertex inside the loop can break this, so it is not always one.
            if ((hIncomingEdge.Face != FaceHandle.Invalid) &&
                 (hOutgoingEdge.Face != FaceHandle.Invalid))
            {
                if (hIncomingEdge.OppositeEdge.NextEdge != hConnectingEdge)
                {
                    return false;
                }

                if (hConnectingEdge.NextEdge != hOutgoingEdge.OppositeEdge)
                {
                    return false;
                }
            }
        }

        hIncomingEdge.Face = FaceHandle.Invalid;
        hOutgoingEdge.Face = FaceHandle.Invalid;

        if (hConnectingEdge.IsValid)
        {
            // The connecting edge's end replaces the outgoing edge's end, so copy the corner data
            CopyFaceVertexData(hConnectingEdge, hOutgoingEdge);
        }
        else
        {
            // Otherwise build a new edge from vertex A to vertex B to replace the two removed edges
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
        {
            return;
        }

        if (!hSrcHalfEdge.IsValid)
        {
            return;
        }

        OnCopyFaceVertexData?.Invoke(hDstHalfEdge, hSrcHalfEdge);
    }

    /// <summary>
    /// Removes a face and the edges only it used. Optionally removes vertices left without edges.
    /// </summary>
    public bool RemoveFace(FaceHandle hFace, bool bRemoveFreeVerts)
    {
        if (!hFace.IsValid)
        {
            return false;
        }

        var hFirstEdge = hFace.Edge;
        if (hFirstEdge.IsValid && (hFirstEdge.Face == hFace))
        {
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

            // Remove edges only this face uses, i.e. whose opposite edge has no face
            for (var iEdge = 0; iEdge < nNumEdges; ++iEdge)
            {
                var hCurrentEdge = pEdgeList[iEdge];

                hCurrentEdge.Face = FaceHandle.Invalid;

                // An open opposite edge would leave the pair without a face, so remove it.
                // An interior edge appears twice in the list; the first visit clears the face from
                // its half edge, so the second visit removes it.
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
        {
            return false;
        }

        var hAdjEdge = hEdge.OppositeEdge;
        var hOppositeEdge = hAdjEdge;

        // A loose edge keeps its connected face, which must stop referring to the removed edge
        var bLooseEdge = IsLooseEdge(GetFullEdgeForHalfEdge(hEdge));

        if ((hEdge.Face.IsValid || hOppositeEdge.Face.IsValid) && (!bLooseEdge))
        {
            // Remove the faces attached to the edge and its opposite edge. Note this will
            // result in RemoveFace() calling RemoveEdge when no more faces are attached
            // to the edge, so we don't actually remove the edge directly here.
            var hFace = hEdge.Face;
            var hAdjFace = hOppositeEdge.Face;
            RemoveFace(hFace, bRemoveFreeVerts);
            RemoveFace(hAdjFace, bRemoveFreeVerts);

            // A face that does not refer back to a corrupt edge leaves it behind, so free it here
            // if it is still in the mesh.
            RemoveHalfEdgePair(hEdge, bRemoveFreeVerts);
        }
        else
        {
            // Keep the face of a loose edge from referring to this edge or its opposite
            var hFace = hEdge.Face;
            if (bLooseEdge && hFace.IsValid)
            {
                var hNextFaceEdge = hFace.Edge;
                while ((hNextFaceEdge == hEdge) || (hNextFaceEdge == hAdjEdge))
                {
                    hNextFaceEdge = hNextFaceEdge.NextEdge;

                    // Came full circle without an edge that stays, so the face is invalid
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

            DetachEdgeFromVertex(hEdge, bRemoveFreeVerts);
            DetachEdgeFromVertex(hEdge.OppositeEdge, bRemoveFreeVerts);

            // Frees the edge and its opposite edge; hEdge is invalid afterwards
            FreeHalfEdgePair(hEdge);
        }

        return true;
    }

    private bool IsLooseEdge(HalfEdgeHandle hFullEdge)
    {
        GetHalfEdgesConnectedToFullEdge(hFullEdge, out var hHalfEdgeA, out var hHalfEdgeB);

        if ((this[hHalfEdgeA].OppositeEdge == this[hHalfEdgeA].NextEdge) ||
             (this[hHalfEdgeB].OppositeEdge == this[hHalfEdgeB].NextEdge))
        {
            return true;
        }

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

            // Removing the last edge can remove the face, so stop once the face handle is invalid
            if (!hFace.IsValid)
            {
                break;
            }

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
                {
                    return hCurrentEdge;
                }

                hCurrentEdge = hCurrentEdge.NextEdge;
            }
            while (hCurrentEdge != hStartEdge);
        }

        return HalfEdgeHandle.Invalid;
    }

    private void FreeVertex(VertexHandle hVertex)
    {
        if (!hVertex.IsValid)
        {
            return;
        }

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
        {
            return false;
        }

        // Both edges must be connected to the same face
        var hFace = hIncomingEdgeA.Face;
        if (hIncomingEdgeB.Face != hFace)
        {
            return false;
        }

        if (!hFace.IsValid)
        {
            return false;
        }

        // Both edges cannot end at the same vertex
        var hVertexA = hIncomingEdgeA.Vertex;
        var hVertexB = hIncomingEdgeB.Vertex;
        if (hVertexA == hVertexB)
        {
            return false;
        }

        // Make sure that an edge connecting the specified vertices does not already exist.
        if (FindFullEdgeConnectingVertices(hVertexA, hVertexB).IsValid)
        {
            return false;
        }

        // Create the new half edge pair
        if (!AllocateHalfEdgePair(out var hNewEdgeAB, out var hNewEdgeBA, hIncomingEdgeB.Index, hIncomingEdgeA.Index))
        {
            return false;
        }

        hNewEdgeAB.Vertex = hVertexB;
        hNewEdgeBA.Vertex = hVertexA;

        // Reconnect the edges
        hNewEdgeAB.NextEdge = hIncomingEdgeB.NextEdge;
        hNewEdgeBA.NextEdge = hIncomingEdgeA.NextEdge;
        hIncomingEdgeA.NextEdge = hNewEdgeAB;
        hIncomingEdgeB.NextEdge = hNewEdgeBA;

        hNewEdgeAB.Face = hFace;
        hFace.Edge = hNewEdgeAB;

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
        {
            return false;
        }

        var nNumFaceEdges = ComputeNumEdgesInFace(hFace);
        if (nNumFaceEdges <= 0)
        {
            return false;
        }

        var vertexList = new VertexHandle[nNumFaceEdges];
        var nVertexCount = 0;
        var hEdge = hFace.Edge;
        do
        {
            vertexList[nVertexCount++] = hEdge.Vertex;
            hEdge = hEdge.NextEdge;
        }
        while (hEdge != hFace.Edge);
        Debug.Assert(nVertexCount == nNumFaceEdges);

        // Collapsing an edge may remove others in the list, and eventually the face itself
        var hCollapsedFaceVertex = VertexHandle.Invalid;
        var hCurrentVertex = vertexList[0];
        for (var iVertex = 1; iVertex < nNumFaceEdges; ++iVertex)
        {
            var hFullEdge = FindFullEdgeConnectingVertices(hCurrentVertex, vertexList[iVertex]);
            if (hFullEdge.IsValid)
            {
                CollapseEdge(hFullEdge, out hCurrentVertex, out var _);

                if (!hCurrentVertex.IsValid)
                {
                    break;
                }
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
        {
            return false;
        }

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

        // Any edge that would overlap after the collapse, and is not on the same face as a
        // collapsed edge, forbids the collapse.
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

                // Overlapping pairs found above are allowed, so skip the face test
                if (((hEdgeAToN == overlappingEdgeA1) && (hEdgeNToB == overlappingEdgeA2)) ||
                     ((hEdgeAToN == overlappingEdgeA2) && (hEdgeNToB == overlappingEdgeA1)))
                {
                    continue;
                }

                if (((hEdgeAToN == overlappingEdgeB1) && (hEdgeNToB == overlappingEdgeB2)) ||
                     ((hEdgeAToN == overlappingEdgeB2) && (hEdgeNToB == overlappingEdgeB1)))
                {
                    continue;
                }

                if (((hEdgeBToN == overlappingEdgeA1) && (hEdgeNToA == overlappingEdgeA2)) ||
                     ((hEdgeBToN == overlappingEdgeA2) && (hEdgeNToA == overlappingEdgeA1)))
                {
                    continue;
                }

                if (((hEdgeBToN == overlappingEdgeB1) && (hEdgeNToA == overlappingEdgeB2)) ||
                     ((hEdgeBToN == overlappingEdgeB2) && (hEdgeNToA == overlappingEdgeB1)))
                {
                    continue;
                }

                if ((pEdgeAToN.Face == pEdgeNToB.Face) && (pEdgeAToN.Face != FaceHandle.Invalid))
                {
                    if ((pEdgeAToN.Face == hFaceA) && ((hEdgeAToN == overlappingEdgeA1) || (hEdgeAToN == overlappingEdgeA2)))
                    {
                        continue;
                    }

                    if ((pEdgeAToN.Face == hFaceB) && ((hEdgeAToN == overlappingEdgeB1) || (hEdgeAToN == overlappingEdgeB2)))
                    {
                        continue;
                    }
                }

                if ((pEdgeBToN.Face == pEdgeNToA.Face) && (pEdgeBToN.Face != FaceHandle.Invalid))
                {
                    if ((pEdgeBToN.Face == hFaceA) && ((hEdgeBToN == overlappingEdgeA1) || (hEdgeBToN == overlappingEdgeA2)))
                    {
                        continue;
                    }

                    if ((pEdgeBToN.Face == hFaceB) && ((hEdgeBToN == overlappingEdgeB1) || (hEdgeBToN == overlappingEdgeB2)))
                    {
                        continue;
                    }
                }

                // Neither path from A to B nor from B to A touches a face of the collapsed edge, so
                // collapsing it would break the topology.
                return false;
            }
        }
        while (hCurrentEdge != hStartEdge);

        if (bCheckOnly)
        {
            return true;
        }

        // Point the edges ending at either old vertex to a new vertex
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (!hNewVertex.IsValid)
        {
            return false;
        }

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
            {
                pFaceA.Edge = hNextEdgeA;
            }

            var pFaceB = hFaceB;
            if (pFaceB.IsValid)
            {
                pFaceB.Edge = hNextEdgeB;
            }

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

        pOutEdgeReplacements = [];

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

        // Collapsing an edge on an interior loop can leave only loose interior edges; remove them
        RemoveLooseEdgesInFace(hFaceA);
        RemoveLooseEdgesInFace(hFaceB);

        Debug.Assert(CheckVertexEdgeIntegrity(hNewVertex));
        Debug.Assert(!hFaceA.IsValid || CheckFaceIntegrity(hFaceA));
        Debug.Assert(!hFaceB.IsValid || CheckFaceIntegrity(hFaceB));

        // The new vertex can be removed along with a triangle if the collapsed edge was on that
        // triangle and not shared with any other face.
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
        {
            return true;
        }

        var hCurrentEdge = hStartEdge;
        do
        {
            if (!CheckEdgeIntegrity(hCurrentEdge, bAssert))
            {
                return false;
            }

            hCurrentEdge = GetNextEdgeInVertexLoop(hCurrentEdge);
        }
        while (hCurrentEdge != hStartEdge);

        return true;
    }

    private bool MergeOverlappingEdges(HalfEdgeHandle hHalfEdgeA, HalfEdgeHandle hHalfEdgeB, out HalfEdgeHandle pOutNewEdge)
    {
        pOutNewEdge = HalfEdgeHandle.Invalid;

        if (!hHalfEdgeA.IsValid || !hHalfEdgeB.IsValid)
        {
            return false;
        }

        // Both edges must refer to each other as the next edge
        Debug.Assert(hHalfEdgeA.NextEdge == hHalfEdgeB);
        Debug.Assert(hHalfEdgeB.NextEdge == hHalfEdgeA);
        if ((hHalfEdgeA.NextEdge != hHalfEdgeB) || (hHalfEdgeB.NextEdge != hHalfEdgeA))
        {
            return false;
        }

        // Both edges must refer to the same face
        Debug.Assert(hHalfEdgeA.Face == hHalfEdgeB.Face);
        if (hHalfEdgeA.Face != hHalfEdgeB.Face)
        {
            return false;
        }

        // The two half edges must be opposites, but not each others opposites
        var hOppositeEdgeA = hHalfEdgeA.OppositeEdge;
        var hOppositeEdgeB = hHalfEdgeB.OppositeEdge;
        Debug.Assert(hOppositeEdgeA.Vertex == hHalfEdgeB.Vertex);
        Debug.Assert(hOppositeEdgeB.Vertex == hHalfEdgeA.Vertex);
        Debug.Assert(hOppositeEdgeA != hOppositeEdgeB);
        if ((hOppositeEdgeA.Vertex != hHalfEdgeB.Vertex) ||
             (hOppositeEdgeB.Vertex != hHalfEdgeA.Vertex) ||
             (hOppositeEdgeA == hOppositeEdgeB))
        {
            return false;
        }

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

        // New pair joining the opposite edges of the two open edges being connected
        if (!AllocateHalfEdgePair(out var hNewHalfEdgeA, out var hNewHalfEdgeB, hOppositeEdgeA.Index, hOppositeEdgeB.Index))
        {
            return false;
        }

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
        Debug.Assert(hEdge.IsValid || (!bAssert));
        if (!hEdge.IsValid)
        {
            return false;
        }

        // 1. Every half edge must be matched with a corresponding opposite half edge to form a pair.
        var hOppositeEdge = GetOppositeHalfEdge(hEdge);
        Debug.Assert(hOppositeEdge.IsValid || (!bAssert));
        if (!hOppositeEdge.IsValid)
        {
            return false;
        }

        Debug.Assert((hOppositeEdge.OppositeEdge == hEdge) || (!bAssert));
        if (hOppositeEdge.OppositeEdge != hEdge)
        {
            return false;
        }

        GetVerticesConnectedToHalfEdge(hEdge, out var hVertexA, out var hVertexB);
        GetVerticesConnectedToHalfEdge(hEdge.OppositeEdge, out var hAdjVertexA, out var hAdjVertexB);
        Debug.Assert((hVertexA == hAdjVertexB) || (!bAssert));
        if (hVertexA != hAdjVertexB)
        {
            return false;
        }

        Debug.Assert((hVertexB == hAdjVertexA) || (!bAssert));
        if (hVertexB != hAdjVertexA)
        {
            return false;
        }

        Debug.Assert((hVertexA != hVertexB) || (!bAssert));
        if (hVertexA == hVertexB)
        {
            return false;
        }

        // 2. Each half edge pair must refer to at least one face.
        Debug.Assert((hEdge.Face != FaceHandle.Invalid) || (hOppositeEdge.Face != FaceHandle.Invalid) || (!bAssert));
        if ((hEdge.Face == FaceHandle.Invalid) && (hOppositeEdge.Face == FaceHandle.Invalid))
        {
            return false;
        }

        // If the half edge refers to a face it must be valid and must refer back to the edge
        if (hEdge.Face != FaceHandle.Invalid)
        {
            // All valid handles within the mesh should always correspond to valid components
            Debug.Assert(hEdge.Face.IsValid || (!bAssert));
            var hFace = hEdge.Face;
            if (!hFace.IsValid)
            {
                return false;
            }

            // An edge must be in the edge loop of the face it is connected to
            var hStartEdge = hFace.Edge;
            var hCurrentEdge = hStartEdge;
            while (hCurrentEdge != hEdge)
            {
                hCurrentEdge = hCurrentEdge.NextEdge;

                // Traversed the whole face edge loop and did not find the edge
                Debug.Assert((hCurrentEdge != hStartEdge) || (!bAssert));
                if (hCurrentEdge == hStartEdge)
                {
                    return false;
                }
            }
        }

        // 3. The next edge reference of an edge must always be valid.
        Debug.Assert((hEdge.NextEdge != HalfEdgeHandle.Invalid) || (!bAssert));
        if (hEdge.NextEdge == HalfEdgeHandle.Invalid)
        {
            return false;
        }

        var hNextEdge = hEdge.NextEdge;
        Debug.Assert(hNextEdge.IsValid || (!bAssert));
        if (!hNextEdge.IsValid)
        {
            return false;
        }

        // 4. The edge specified by the next edge reference must refer to the same face as this edge.
        Debug.Assert((hEdge.Face == hNextEdge.Face) || (!bAssert));
        if (hEdge.Face != hNextEdge.Face)
        {
            return false;
        }

        // 5. An edge may not refer to its opposite edge as it next edge.
        Debug.Assert((hNextEdge != hOppositeEdge) || (!bAssert));
        if (hNextEdge == hOppositeEdge)
        {
            return false;
        }

        // 6. The vertex reference of and edge must always be valid
        Debug.Assert((hEdge.Vertex != VertexHandle.Invalid) || (!bAssert));
        if (hEdge.Vertex == VertexHandle.Invalid)
        {
            return false;
        }

        var hVertex = hEdge.Vertex;
        Debug.Assert(hVertex.IsValid || (!bAssert));
        if (!hVertex.IsValid)
        {
            return false;
        }

        // 7. Both half edges of a pair may not specify the same vertex
        Debug.Assert((hEdge.Vertex != hOppositeEdge.Vertex) || (!bAssert));
        if (hEdge.Vertex == hOppositeEdge.Vertex)
        {
            return false;
        }

        // 8. The opposite edge starts at the edge's end vertex, so it is in that vertex's loop
        Debug.Assert((hVertex.Edge != HalfEdgeHandle.Invalid) || (!bAssert));
        if (hVertex.Edge == HalfEdgeHandle.Invalid)
        {
            return false;
        }

        var bFoundOpposite = false;
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

        Debug.Assert((bFoundOpposite) || (!bAssert));
        if (!bFoundOpposite)
        {
            return false;
        }

        // 9. There may never be two edges which start and end at the same vertex.
        var hOverlappingEdge = FindOverlappingEdge(hEdge);
        Debug.Assert(!hOverlappingEdge.IsValid || (!bAssert));
        if (hOverlappingEdge.IsValid)
        {
            return false;
        }

        return true;
    }

    private static HalfEdgeHandle FindOverlappingEdge(HalfEdgeHandle hHalfEdge)
    {
        if (!hHalfEdge.IsValid)
        {
            return HalfEdgeHandle.Invalid;
        }

        // Find another edge from the start vertex that ends at the same vertex
        var hVertex = hHalfEdge.OppositeEdge.Vertex;
        var hCurrentEdge = hVertex.Edge;
        do
        {
            if (hCurrentEdge != hHalfEdge)
            {
                if (hCurrentEdge.Vertex == hHalfEdge.Vertex)
                {
                    return hCurrentEdge;
                }
            }
            hCurrentEdge = hCurrentEdge.OppositeEdge.NextEdge;
        }
        while (hCurrentEdge != hVertex.Edge);

        return HalfEdgeHandle.Invalid;
    }

    private static void DetachFaceFromEdges(FaceHandle hFace)
    {
        if (!hFace.IsValid)
        {
            return;
        }

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
        {
            return;
        }

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
        {
            return false;
        }

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
        {
            return false;
        }

        // The opposite edges of the open half edges must not belong to the same face
        if (hOpenHalfEdgeA.OppositeEdge.Face == hOpenHalfEdgeB.OppositeEdge.Face)
        {
            return false;
        }

        // Two edges which start or end at the same vertex may not be merged
        if (hOpenHalfEdgeA.Vertex == hOpenHalfEdgeB.Vertex)
        {
            return false;
        }

        if (hOpenHalfEdgeA.OppositeEdge.Vertex == hOpenHalfEdgeB.OppositeEdge.Vertex)
        {
            return false;
        }

        if (!GetEdgeMergeVertexPairs(hEdgeA, hEdgeB, out var vertexPairA1, out var vertexPairA2, out var vertexPairB1, out var vertexPairB2))
        {
            return false;
        }

        // If either of the vertices are shared already, just merge the other pair of vertices.
        if (vertexPairA1 == vertexPairA2)
        {
            hOutNewVertexA = vertexPairA1;
            return MergeVertices(vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out hOutNewVertexB, false);
        }

        if (vertexPairB1 == vertexPairB2)
        {
            hOutNewVertexB = vertexPairB1;
            return MergeVertices(vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out hOutNewVertexA, false);
        }

        // Check both vertex pairs first so the edge never merges only one of its vertices
        if ((!MergeVertices(vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out _, true)) ||
             (!MergeVertices(vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out _, true)))
        {
            return false;
        }

        if (!MergeVertices(vertexPairA1, vertexPairA2, hOpenHalfEdgeA.NextEdge, hOpenHalfEdgeB, out hOutNewVertexA, false))
        {
            return false;
        }

        if (!MergeVertices(vertexPairB1, vertexPairB2, hOpenHalfEdgeA, hOpenHalfEdgeB.NextEdge, out hOutNewVertexB, false))
        {
            return false;
        }

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

        // Without a given open edge, the vertex must have exactly one open edge to use
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
        {
            return false;
        }

        // A pair of open edges connecting the vertices: add a triangle and collapse its new edge
        // to merge them.
        {
            // Open edge from vertex N (the end of the open edge) to vertex B
            var hVertexN = hOpenEdgeA.Vertex;
            var hEdgeNToB = FindHalfEdgeConnectingVertices(hVertexN, hVertexB);
            if (hEdgeNToB != HalfEdgeHandle.Invalid)
            {
                // If there is an edge but it is not open the vertices cannot be merged
                if (hEdgeNToB.Face != FaceHandle.Invalid)
                {
                    return false;
                }

                if (!AddFace(out var hNewFace, hVertexA, hVertexN, hVertexB))
                {
                    return false;
                }

                hFullEdge = FindFullEdgeConnectingVertices(hVertexA, hVertexB);
                var bSuccess = CollapseEdge(hFullEdge, out hOutNewVertex, bCheckOnly, out var _);
                // Remove the temporary triangle on failure too, otherwise the mesh gets
                // polluted by hard to find ghost triangles
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
                {
                    return false;
                }

                if (!AddFace(out var hNewFace, hVertexB, hVertexM, hVertexA))
                {
                    return false;
                }

                hFullEdge = FindFullEdgeConnectingVertices(hVertexA, hVertexB);
                var bSuccess = CollapseEdge(hFullEdge, out hOutNewVertex, bCheckOnly, out var _);
                // Remove the temporary triangle on failure too, otherwise the mesh gets
                // polluted by hard to find ghost triangles
                if (bCheckOnly || !bSuccess)
                {
                    RemoveFace(hNewFace, false);
                }
                return bSuccess;
            }
        }

        // No single edge or open edge pair connects them. They may merge if they share no face
        // and no edge pair connects them.
        var hClosedEdgeA = hOpenEdgeA.OppositeEdge;
        var hClosedEdgeB = hOpenEdgeB.OppositeEdge;
        if (hClosedEdgeA.Face == hClosedEdgeB.Face)
        {
            return false;
        }

        if (AreVerticesConnectedByEdgePair(hVertexA, hVertexB))
        {
            return false;
        }

        // Previous edges whose next edge is the open edge; these are open too
        var hPreviousOpenEdgeA = FindPreviousEdgeInFaceLoop(hOpenEdgeA);
        var hPreviousOpenEdgeB = FindPreviousEdgeInFaceLoop(hOpenEdgeB);
        var pPreviousOpenEdgeA = hPreviousOpenEdgeA;
        var pPreviousOpenEdgeB = hPreviousOpenEdgeB;
        Debug.Assert(pPreviousOpenEdgeA.IsValid);
        Debug.Assert(pPreviousOpenEdgeB.IsValid);
        if (!pPreviousOpenEdgeA.IsValid || !pPreviousOpenEdgeB.IsValid)
        {
            return false;
        }

        if (bCheckOnly)
        {
            return true;
        }

        // Failing these means broken topology or a bug in FindPreviousEdgeInFaceLoop()
        Debug.Assert(pPreviousOpenEdgeA.Vertex == hVertexA);
        Debug.Assert(pPreviousOpenEdgeB.Vertex == hVertexB);
        Debug.Assert(pPreviousOpenEdgeA.NextEdge == hOpenEdgeA);
        Debug.Assert(pPreviousOpenEdgeB.NextEdge == hOpenEdgeB);
        Debug.Assert(pPreviousOpenEdgeA.Face == FaceHandle.Invalid);
        Debug.Assert(pPreviousOpenEdgeB.Face == FaceHandle.Invalid);

        // Point the edges ending at either old vertex to a new vertex
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (hNewVertex == VertexHandle.Invalid)
        {
            return false;
        }

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
                {
                    return hEdge;
                }

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
            {
                return true;
            }

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
        {
            return false;
        }

        GetVerticesConnectedToHalfEdge(hExistingEdgeA, out var hVertexA, out var hVertexB);

        var hExistingEdgeB = hExistingEdgeA.OppositeEdge;
        Debug.Assert(hExistingEdgeA.Vertex == hVertexB);
        Debug.Assert(hExistingEdgeB.Vertex == hVertexA);

        var hPrevEdgeB = FindPreviousEdgeInFaceLoop(hExistingEdgeB);
        Debug.Assert(hPrevEdgeB.IsValid);

        // New edges copy the existing edges' data so face-vertex attributes (colors, UVs, etc.)
        // are preserved on the new segments.
        if (!AllocateHalfEdgePair(out var hNewEdgeA, out var hNewEdgeB, hExistingEdgeA.Index, hExistingEdgeB.Index))
        {
            return false;
        }

        // Create the new vertex
        var hNewVertex = AllocateVertex(Vertex.Invalid);
        if (!hNewVertex.IsValid)
        {
            return false;
        }

        hExistingEdgeA.Vertex = hNewVertex;

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
            {
                VertexList[hVertex.Index] = value;
            }
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
            {
                FaceList[hFace.Index] = value;
            }
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
            {
                HalfEdgeList[hEdge.Index] = value;
            }
        }
    }
#pragma warning restore CA1043
}


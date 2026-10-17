using System.Linq;

namespace ValveResourceFormat.IO.ContentFormats.HalfEdgeMesh;

public partial class HalfEdgeMesh
{
    /// <summary>
    /// Fills an empty mesh from the index arrays a mesh is stored as. Every vertex, half edge and face keeps
    /// the index it has in the arrays, and an index that is out of range becomes an invalid reference. Nothing
    /// is checked beyond that, see <see cref="RemoveInvalidComponents"/>.
    /// </summary>
    /// <param name="vertexEdges">A half edge starting at each vertex.</param>
    /// <param name="edgeVertices">The vertex each half edge ends at.</param>
    /// <param name="edgeOpposites">The half edge running the other way along each half edge.</param>
    /// <param name="edgeNexts">The half edge following each half edge around its face.</param>
    /// <param name="edgeFaces">The face each half edge runs around, -1 for the outer side of an open edge.</param>
    /// <param name="faceEdges">A half edge of each face.</param>
    /// <returns>Whether the arrays of the half edges are as long as each other. The mesh stays empty when they are not.</returns>
    /// <exception cref="InvalidOperationException">The mesh is not empty.</exception>
    public bool Deserialize(
        ReadOnlySpan<int> vertexEdges,
        ReadOnlySpan<int> edgeVertices,
        ReadOnlySpan<int> edgeOpposites,
        ReadOnlySpan<int> edgeNexts,
        ReadOnlySpan<int> edgeFaces,
        ReadOnlySpan<int> faceEdges)
    {
        if (VertexCount != 0 || HalfEdgeCount != 0 || FaceCount != 0)
        {
            throw new InvalidOperationException("Topology can only be read into an empty mesh.");
        }

        var vertexCount = vertexEdges.Length;
        var edgeCount = edgeVertices.Length;
        var faceCount = faceEdges.Length;

        if (edgeOpposites.Length != edgeCount || edgeNexts.Length != edgeCount || edgeFaces.Length != edgeCount)
        {
            return false;
        }

        VertexList.AllocateMultiple(vertexCount, Vertex.Invalid);
        HalfEdgeList.AllocateMultiple(edgeCount, HalfEdge.Invalid);
        FaceList.AllocateMultiple(faceCount, Face.Invalid);

        for (var i = 0; i < vertexCount; i++)
        {
            VertexList[i] = new Vertex { Edge = InRange(vertexEdges[i], edgeCount) };
        }

        for (var i = 0; i < edgeCount; i++)
        {
            HalfEdgeList[i] = new HalfEdge
            {
                Vertex = InRange(edgeVertices[i], vertexCount),
                OppositeEdge = InRange(edgeOpposites[i], edgeCount),
                NextEdge = InRange(edgeNexts[i], edgeCount),
                Face = InRange(edgeFaces[i], faceCount),
            };
        }

        for (var i = 0; i < faceCount; i++)
        {
            FaceList[i] = new Face { Edge = InRange(faceEdges[i], edgeCount) };
        }

        return true;

        static int InRange(int index, int count) => (uint)index < (uint)count ? index : -1;
    }

    /// <summary>
    /// Removes what does not hold together as a mesh, until everything left does: faces whose loop of half edges
    /// is broken, and edges that fail the checks an edge has to pass. Two edges between the same pair of vertices
    /// are merged into one when they enclose nothing, any other failing edge is removed with the faces on it.
    /// </summary>
    public void RemoveInvalidComponents()
    {
        FreeDanglingComponents();

        bool removedAny;

        do
        {
            removedAny = false;

            foreach (var hFace in FaceHandles.ToArray())
            {
                if (!CheckFaceIntegrity(hFace, false) && RemoveFace(hFace, true))
                {
                    removedAny = true;
                }
            }

            foreach (var hEdge in HalfEdgeHandles.ToArray())
            {
                if (!hEdge.IsValid || CheckEdgeIntegrity(hEdge, false))
                {
                    continue;
                }

                if (MergeWithOverlappingEdge(hEdge) || RemoveEdge(GetFullEdge(hEdge), true))
                {
                    removedAny = true;
                }
            }
        }
        while (removedAny);
    }

    // Two edges between the same vertices can be merged when one side of each closes a loop with the other
    private bool MergeWithOverlappingEdge(HalfEdgeHandle hEdge)
    {
        var hOverlappingEdge = FindOverlappingEdge(hEdge);

        if (!hOverlappingEdge.IsValid)
        {
            return false;
        }

        var hOppositeEdge = hEdge.OppositeEdge;

        if (hOppositeEdge.NextEdge == hOverlappingEdge && hOverlappingEdge.NextEdge == hOppositeEdge)
        {
            return CanMergeOverlappingEdges(hOppositeEdge, hOverlappingEdge) && MergeOverlappingEdges(hOppositeEdge, hOverlappingEdge, out _);
        }

        var hOverlappingOpposite = hOverlappingEdge.OppositeEdge;

        if (hEdge.NextEdge == hOverlappingOpposite && hOverlappingOpposite.NextEdge == hEdge)
        {
            return CanMergeOverlappingEdges(hEdge, hOverlappingOpposite) && MergeOverlappingEdges(hEdge, hOverlappingOpposite, out _);
        }

        return false;
    }

    // The conditions MergeOverlappingEdges asserts on, for a merge that is only being tried
    private static bool CanMergeOverlappingEdges(HalfEdgeHandle hHalfEdgeA, HalfEdgeHandle hHalfEdgeB)
    {
        var hOppositeEdgeA = hHalfEdgeA.OppositeEdge;
        var hOppositeEdgeB = hHalfEdgeB.OppositeEdge;

        return hHalfEdgeA.Face == hHalfEdgeB.Face
            && hOppositeEdgeA.Vertex == hHalfEdgeB.Vertex
            && hOppositeEdgeB.Vertex == hHalfEdgeA.Vertex
            && hOppositeEdgeA != hOppositeEdgeB;
    }

    // An edge is known by the lower of its two half edges
    private static HalfEdgeHandle GetFullEdge(HalfEdgeHandle hEdge)
    {
        var hOppositeEdge = hEdge.OppositeEdge;

        return hOppositeEdge.IsValid && hEdge.Index >= hOppositeEdge.Index ? hOppositeEdge : hEdge;
    }

    // Walking around a face or a vertex only ends when every half edge has a valid opposite that points back at
    // it, and is the next of exactly one half edge. Half edges read from a file that break this cannot be
    // reasoned about any further, and are dropped along with the half edges that lead to them.
    private void FreeDanglingComponents()
    {
        var predecessors = new int[HalfEdgeCount];
        bool freedAny;

        do
        {
            freedAny = false;
            Array.Clear(predecessors);

            foreach (var i in HalfEdgeList.ActiveList)
            {
                var next = HalfEdgeList[i].NextEdge;

                if (IsHalfEdgeAllocated(next))
                {
                    predecessors[next]++;
                }
            }

            foreach (var i in HalfEdgeList.ActiveList.ToArray())
            {
                var halfEdge = HalfEdgeList[i];

                if (predecessors[i] == 1
                    && IsVertexAllocated(halfEdge.Vertex)
                    && IsHalfEdgeAllocated(halfEdge.NextEdge)
                    && IsHalfEdgeAllocated(halfEdge.OppositeEdge)
                    && halfEdge.OppositeEdge != i
                    && HalfEdgeList[halfEdge.OppositeEdge].OppositeEdge == i)
                {
                    continue;
                }

                HalfEdgeList.Deallocate(i);
                freedAny = true;
            }
        }
        while (freedAny);

        // What is left refers to a half edge that is there, or to none
        foreach (var i in FaceList.ActiveList)
        {
            if (!IsHalfEdgeAllocated(FaceList[i].Edge))
            {
                FaceList[i] = Face.Invalid;
            }
        }

        foreach (var i in VertexList.ActiveList)
        {
            if (!IsHalfEdgeAllocated(VertexList[i].Edge))
            {
                VertexList[i] = Vertex.Invalid;
            }
        }
    }
}

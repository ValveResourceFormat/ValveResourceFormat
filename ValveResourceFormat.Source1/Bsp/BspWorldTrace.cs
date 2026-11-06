using System.Buffers.Binary;

namespace ValveResourceFormat.Source1.Bsp;

/// <summary>
/// Traces line segments through the node tree of the world model, against the leaves that block sight.
/// </summary>
internal sealed class BspWorldTrace
{
    private const int NodeSize = 32;
    private const int WideNodeSize = 48;

    // Solid, opaque and moveable contents block the traces that light models
    private const int BlockingContents = 0x4081;

    // How far a face may be from the point of a hit to count as the face that was hit
    private const float FaceTolerance = 1f;

    /// <summary>A node of the tree. A negative child is a leaf, <c>-1 - child</c>.</summary>
    internal readonly record struct Node(int Plane, int Child0, int Child1, int FirstFace, int FaceCount);

    // The lumps are read in the constructor, so traces only read immutable data and can run on several threads
    private readonly Node[] nodes;
    private readonly BspPlane[] planes;
    private readonly BspLeaf[] leafs;
    private readonly BspFace[] faces;
    private readonly BspTextureInfo[] textureInfos;
    private readonly int[] surfEdges;
    private readonly BspEdge[] edges;
    private readonly Vector3[] vertices;
    private readonly bool hasTree;
    private readonly int headNode;

    /// <summary>Reads the nodes of a map. Traces can run on several threads at once.</summary>
    /// <param name="bsp">The map.</param>
    public BspWorldTrace(BspFile bsp)
    {
        nodes = ReadNodes(bsp);
        planes = bsp.Planes;
        leafs = bsp.Leafs;
        faces = bsp.Faces;
        textureInfos = bsp.TextureInfos;
        surfEdges = bsp.SurfEdges;
        edges = bsp.Edges;
        vertices = bsp.Vertices;
        hasTree = nodes.Length > 0 && bsp.Models.Length > 0;

        if (hasTree)
        {
            headNode = bsp.Models[0].HeadNode;
        }
    }

    /// <summary>Reads the nodes of the trees of every model of a map.</summary>
    /// <param name="bsp">The map.</param>
    /// <returns>The nodes.</returns>
    internal static Node[] ReadNodes(BspFile bsp)
    {
        if (bsp.IsWide)
        {
            return ReadWideNodes(bsp.GetVersionedLumpData(LumpType.Nodes, 1).Span);
        }

        var data = bsp.GetVersionedLumpData(LumpType.Nodes, 0).Span;
        var nodes = new Node[data.Length / NodeSize];

        for (var i = 0; i < nodes.Length; i++)
        {
            var entry = data.Slice(i * NodeSize, NodeSize);

            nodes[i] = new Node(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[24..]),
                BinaryPrimitives.ReadUInt16LittleEndian(entry[26..]));
        }

        return nodes;
    }

    // Nodes of maps with 32-bit indices store the bounds as floats
    private static Node[] ReadWideNodes(ReadOnlySpan<byte> data)
    {
        var nodes = new Node[data.Length / WideNodeSize];

        for (var i = 0; i < nodes.Length; i++)
        {
            var entry = data.Slice(i * WideNodeSize, WideNodeSize);

            nodes[i] = new Node(
                BinaryPrimitives.ReadInt32LittleEndian(entry),
                BinaryPrimitives.ReadInt32LittleEndian(entry[4..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[8..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[36..]),
                BinaryPrimitives.ReadInt32LittleEndian(entry[40..]));
        }

        return nodes;
    }

    /// <summary>The result of a trace.</summary>
    /// <param name="Fraction">How far along the segment the first blocking leaf starts, 1 when nothing blocks it.</param>
    /// <param name="Node">The node whose plane the segment crossed into the blocking leaf, or -1 when it started in one.</param>
    public readonly record struct Hit(float Fraction, int Node);

    /// <summary>Traces from start to end and finds where the segment first enters a leaf that blocks sight.</summary>
    /// <param name="start">The start of the segment.</param>
    /// <param name="end">The end of the segment.</param>
    /// <returns>The hit, with a fraction of 1 when nothing blocks the segment.</returns>
    public Hit Trace(Vector3 start, Vector3 end)
    {
        if (!hasTree)
        {
            return new Hit(1f, -1);
        }

        var hit = new Hit(1f, -1);

        if (!TraceNode(headNode, 0f, 1f, start, end, ref hit))
        {
            return new Hit(1f, -1);
        }

        return hit;
    }

    // Walks the near side of every crossed plane first, so the first blocking leaf found is the nearest one
    private bool TraceNode(int nodeIndex, float startFraction, float endFraction, Vector3 start, Vector3 end, ref Hit hit)
    {
        if (nodeIndex < 0)
        {
            if ((leafs[-1 - nodeIndex].Contents & BlockingContents) == 0)
            {
                return false;
            }

            hit = new Hit(startFraction, -1);
            return true;
        }

        ref readonly var node = ref nodes[nodeIndex];
        ref readonly var plane = ref planes[node.Plane];
        var startDistance = Vector3.Dot(plane.Normal, start) - plane.Dist;
        var endDistance = Vector3.Dot(plane.Normal, end) - plane.Dist;

        if (startDistance >= 0f && endDistance >= 0f)
        {
            return TraceNode(node.Child0, startFraction, endFraction, start, end, ref hit);
        }

        if (startDistance < 0f && endDistance < 0f)
        {
            return TraceNode(node.Child1, startFraction, endFraction, start, end, ref hit);
        }

        var split = Math.Clamp(startDistance / (startDistance - endDistance), 0f, 1f);
        var middleFraction = startFraction + ((endFraction - startFraction) * split);
        var middle = Vector3.Lerp(start, end, split);
        var nearChild = startDistance >= 0f ? node.Child0 : node.Child1;
        var farChild = startDistance >= 0f ? node.Child1 : node.Child0;

        if (TraceNode(nearChild, startFraction, middleFraction, start, middle, ref hit))
        {
            return true;
        }

        if (!TraceNode(farChild, middleFraction, endFraction, middle, end, ref hit))
        {
            return false;
        }

        // The deepest crossing at the start of the blocking leaf is the plane of the surface that was hit
        if (hit.Node == -1 && hit.Fraction == middleFraction)
        {
            hit = hit with { Node = nodeIndex };
        }

        return true;
    }

    /// <summary>Gets whether a hit landed on a face that shows the sky.</summary>
    /// <param name="hit">The hit of a trace.</param>
    /// <param name="point">Where the hit is in the world.</param>
    public bool IsSkyHit(Hit hit, Vector3 point)
    {
        if (hit.Node < 0)
        {
            return false;
        }

        var node = nodes[hit.Node];

        for (var i = 0; i < node.FaceCount; i++)
        {
            var face = faces[node.FirstFace + i];

            if (face.TexInfo < 0 || (textureInfos[face.TexInfo].Flags & SurfFlags.Sky) == 0)
            {
                continue;
            }

            if (ContainsPoint(face, point))
            {
                return true;
            }
        }

        return false;
    }

    private Vector3 GetVertexFromSurfEdge(int surfEdgeIndex)
    {
        var surfEdge = surfEdges[surfEdgeIndex];
        var edge = edges[Math.Abs(surfEdge)];
        return vertices[surfEdge >= 0 ? edge.A : edge.B];
    }

    // Whether the point lies within the edges of a convex face, seen along the normal of its plane
    private bool ContainsPoint(in BspFace face, Vector3 point)
    {
        var normal = planes[face.PlaneNum].Normal;
        var hasInside = false;
        var hasOutside = false;

        for (var i = 0; i < face.NumEdges; i++)
        {
            var from = GetVertexFromSurfEdge(face.FirstEdge + i);
            var to = GetVertexFromSurfEdge(face.FirstEdge + ((i + 1) % face.NumEdges));
            var edge = to - from;
            var edgeNormal = Vector3.Normalize(Vector3.Cross(edge, normal));
            var side = Vector3.Dot(point - from, edgeNormal);

            if (side > FaceTolerance)
            {
                hasOutside = true;
            }
            else if (side < -FaceTolerance)
            {
                hasInside = true;
            }

            if (hasInside && hasOutside)
            {
                return false;
            }
        }

        return true;
    }
}

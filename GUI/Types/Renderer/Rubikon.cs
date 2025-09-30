using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace GUI.Types.Renderer;

class Rubikon
{
    private const int STACK_SIZE = 64;

    public record PhysicsMeshData(
        Vector3[] VertexPositions,
        Triangle[] Triangles,
        Node[] PhysicsTree
    );

    public record PhysicsHullData(
        Vector3 Min,
        Vector3 Max,
        Vector3[] VertexPositions,
        Hull.HalfEdge[] HalfEdges,
        byte[] FaceEdgeIndices,
        Hull.Plane[] Planes
    );

    public PhysicsMeshData[] Meshes { get; }
    public PhysicsHullData[] Hulls { get; }

    public Rubikon(PhysAggregateData physicsData)
    {
        var worldMeshes = physicsData.Parts[0].Shape.Meshes
            .Where(m => physicsData.CollisionAttributes[m.CollisionAttributeIndex].GetStringProperty("m_CollisionGroupString") == "Default")
            .ToArray();

        Meshes = new PhysicsMeshData[worldMeshes.Length];
        var meshIndex = 0;

        foreach (var mesh in worldMeshes)
        {
            var vertexPositions = mesh.Shape.GetVertices();
            var triangles = mesh.Shape.GetTriangles();
            var physicsTree = mesh.Shape.ParseNodes();

            Meshes[meshIndex++] = new PhysicsMeshData([.. vertexPositions], [.. triangles], [.. physicsTree]);
        }

        Hulls = new PhysicsHullData[physicsData.Parts[0].Shape.Hulls.Length];
        var hullIndex = 0;
        foreach (var hullDesc in physicsData.Parts[0].Shape.Hulls)
        {
            var hull = hullDesc.Shape;
            var vertexPositions = hull.GetVertexPositions();
            var halfEdges = hull.GetEdges();
            var faceEdgeIndices = hull.GetFaces();
            var planes = hull.GetPlanes();


            Hulls[hullIndex++] = new PhysicsHullData(
                hull.Min, hull.Max,
                [.. vertexPositions],
                [.. halfEdges],
                [.. MemoryMarshal.Cast<Hull.Face, byte>(faceEdgeIndices)],
                [.. planes]
            );
        }

        Hulls = physicsData.Parts[0].Shape.Hulls;
    }

    public record struct TraceResult(bool Hit, Vector3 HitPosition, Vector3 HitNormal, float Distance, int TriangleIndex)
    {
        public TraceResult() : this(false, Vector3.Zero, Vector3.UnitZ, float.MaxValue, -1) { }
    }

    public readonly struct RayTraceContext
    {
        public Vector3 Origin { get; }
        public Vector3 Direction { get; }
        public Vector3 InvDirection { get; }
        public float Length { get; }

        public readonly Vector3 EndPosition => Origin + Direction * Length;

        public RayTraceContext(Vector3 start, Vector3 end)
        {
            Origin = start;
            Direction = Vector3.Normalize(end - start);
            InvDirection = Vector3.One / Direction;
            Length = Vector3.Distance(start, end);
        }
    }

    public TraceResult TraceRay(Vector3 from, Vector3 to)
    {
        TraceResult closestHit = new();

        RayTraceContext ray = new(from, to);

        foreach (var mesh in Meshes)
        {
            var hit = RayIntersectsWithMesh(ray, mesh);
            if (hit.Hit && hit.Distance < closestHit.Distance)
            {
                closestHit = hit;
            }
        }

        foreach (var hull in Hulls)
        {
            var hit = RayIntersectsWithHull(ray, hull);
            if (hit.Hit && hit.Distance < closestHit.Distance)
            {
                closestHit = hit;
            }
        }

        return closestHit;
    }

    private static TraceResult RayIntersectsWithHull(RayTraceContext ray, PhysicsHullData hull)
    {
        var closestHit = new TraceResult();

        if (!RayIntersectsAABB(ray, hull.Min, hull.Max))
        {
            return closestHit;
        }

        foreach (var firstEdgeCcw in hull.FaceEdgeIndices)
        {
            var firstEdge = hull.HalfEdges[firstEdgeCcw];
            var edgeIndex = firstEdge.Next;
            var v0 = hull.VertexPositions[firstEdge.Origin];

            do
            {
                var edge1 = hull.HalfEdges[edgeIndex];
                var edge2 = hull.HalfEdges[edge1.Next];

                // Just do triangle intersection?
                var v1 = hull.VertexPositions[edge1.Origin];
                var v2 = hull.VertexPositions[edge2.Origin];

                if (RayIntersectsTriangle(ray, v0, v1, v2, out var intersection))
                {
                    // Update if this is the closest hit
                    if (intersection.Distance < closestHit.Distance)
                    {
                        closestHit = new(true, ray.Origin + ray.Direction * intersection.Distance, intersection.Normal, intersection.Distance, -1);
                    }
                }

                edgeIndex = edge1.Next;
            } while (edgeIndex != firstEdgeCcw);
        }

        return closestHit;
    }

    private static TraceResult RayIntersectsWithMesh(RayTraceContext ray, PhysicsMeshData mesh)
    {
        Span<(Node Node, int Index)> stack = stackalloc (Node Node, int Index)[STACK_SIZE];
        var stackCount = 0;
        stack[stackCount++] = (mesh.PhysicsTree[0], 0);

        var closestHit = new TraceResult();

        while (stackCount > 0)
        {
            var nodeWithIndex = stack[--stackCount];
            var node = nodeWithIndex.Node;
            if (!RayIntersectsAABB(ray, node.Min, node.Max))
            {
                continue;
            }

            if (node.Type != NodeType.Leaf)
            {
                var leftChild = nodeWithIndex.Index + 1;
                var rightChild = nodeWithIndex.Index + (int)node.ChildOffset;

                var rayIsPositive = ray.Direction[(int)node.Type] >= 0;
                var (nearId, farId) = rayIsPositive
                    ? (leftChild, rightChild)    // Ray going positive direction, traverse left first
                    : (rightChild, leftChild);   // Ray going negative direction, traverse right first

                // Push far node first so near node is processed first (stack is LIFO)
                stack[stackCount++] = new(mesh.PhysicsTree[farId], farId);
                stack[stackCount++] = new(mesh.PhysicsTree[nearId], nearId);
                continue;
            }

            // Check triangles in this leaf node
            var count = (int)node.ChildOffset;
            var startIndex = (int)node.TriangleOffset;

            for (var i = startIndex; i < startIndex + count; i++)
            {
                var triangle = mesh.Triangles[i];
                var v0 = mesh.VertexPositions[triangle.X];
                var v1 = mesh.VertexPositions[triangle.Y];
                var v2 = mesh.VertexPositions[triangle.Z];

                if (!RayIntersectsTriangle(ray, v0, v1, v2, out var intersection))
                {
                    continue;
                }

                // Update if this is the closest hit
                if (intersection.Distance < closestHit.Distance)
                {
                    closestHit = new(true, ray.Origin + ray.Direction * intersection.Distance, intersection.Normal, intersection.Distance, i);
                }
            }
        }

        return closestHit;
    }

    private static bool RayIntersectsAABB(RayTraceContext ray, Vector3 min, Vector3 max)
    {
        // Calculate intersection with AABB using slab method
        var t1 = (min - ray.Origin) * ray.InvDirection;
        var t2 = (max - ray.Origin) * ray.InvDirection;

        var tNear = Vector3.Min(t1, t2);
        var tFar = Vector3.Max(t1, t2);

        var tNearMax = MathF.Max(tNear.X, MathF.Max(tNear.Y, tNear.Z));
        var tFarMin = MathF.Min(tFar.X, MathF.Min(tFar.Y, tFar.Z));

        var intersects = tNearMax <= tFarMin && tFarMin >= 0 && tNearMax <= ray.Length;
        return intersects;
    }

    private static bool RayIntersectsTriangle(RayTraceContext ray, Vector3 v0, Vector3 v1, Vector3 v2, out (float Distance, Vector3 Normal) intersection)
    {
        // Möller–Trumbore ray-triangle intersection algorithm
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var h = Vector3.Cross(ray.Direction, edge2);
        var a = Vector3.Dot(edge1, h);

        intersection = (-1, Vector3.Zero);

        // Ray is parallel to triangle
        if (Math.Abs(a) < 1e-6f)
        {
            return false;
        }

        var f = 1.0f / a;
        var s = ray.Origin - v0;
        var u = f * Vector3.Dot(s, h);

        // Ray intersection is outside triangle
        if (u is < 0.0f or > 1.0f)
        {
            return false;
        }

        var q = Vector3.Cross(s, edge1);
        var v = f * Vector3.Dot(ray.Direction, q);

        // Ray intersection is outside triangle
        if (v < 0.0f || u + v > 1.0f)
        {
            return false;
        }

        var t = f * Vector3.Dot(edge2, q);

        // Ray intersection is behind ray origin or beyond ray end
        if (t < 0 || t > ray.Length)
        {
            return false;
        }

        intersection = (t, Vector3.Normalize(Vector3.Cross(edge1, edge2)));
        return true;
    }
}

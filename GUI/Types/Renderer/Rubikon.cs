
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

    public readonly struct AABBTraceContext
    {
        public Vector3 Origin { get; }
        public Vector3 End { get; }
        public Vector3 Direction { get; }
        public Vector3 HalfExtents { get; }
        public float Length { get; }

        public AABBTraceContext(Vector3 start, Vector3 end, Vector3 halfExtents)
        {
            Origin = start;
            End = end;
            Direction = Vector3.Normalize(end - start);
            HalfExtents = halfExtents;
            Length = Vector3.Distance(start, end);
        }
    }

    public TraceResult TraceAABB(Vector3 from, Vector3 to, AABB aabb)
    {
        TraceResult closestHit = new();
        var halfExtents = aabb.Size * 0.5f;
        var trace = new AABBTraceContext(from, to, halfExtents);

        // Check against all meshes
        foreach (var mesh in Meshes)
        {
            var hit = AABBTraceMesh(trace, mesh);
            if (hit.Hit && hit.Distance < closestHit.Distance)
            {
                closestHit = hit;

                // Early out if we hit something very close to start
                if (hit.Distance < 1e-4f)
                {
                    break;
                }
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

    private static TraceResult AABBTraceMesh(AABBTraceContext trace, PhysicsMeshData mesh)
    {
        Span<(Node Node, int Index)> stack = stackalloc (Node Node, int Index)[STACK_SIZE];
        var stackCount = 0;
        stack[stackCount++] = (mesh.PhysicsTree[0], 0);

        var closestHit = new TraceResult();

        var ray = new RayTraceContext(trace.Origin, trace.End);

        while (stackCount > 0)
        {
            var nodeWithIndex = stack[--stackCount];
            var node = nodeWithIndex.Node;

            // Expand node AABB by trace half extents for conservative culling
            if (!RayIntersectsAABB(ray, node.Min - trace.HalfExtents, node.Max + trace.HalfExtents))
            {
                continue;
            }

            if (node.Type != NodeType.Leaf)
            {
                var leftChild = nodeWithIndex.Index + 1;
                var rightChild = nodeWithIndex.Index + (int)node.ChildOffset;

                var rayIsPositive = ray.Direction[(int)node.Type] >= 0;
                var (nearId, farId) = rayIsPositive
                    ? (leftChild, rightChild)
                    : (rightChild, leftChild);

                // Push far node first so near node is processed first (stack is LIFO)
                stack[stackCount++] = new(mesh.PhysicsTree[farId], farId);
                stack[stackCount++] = new(mesh.PhysicsTree[nearId], nearId);
                continue;
            }

            // Process triangles in leaf node
            var count = (int)node.ChildOffset;
            var startIndex = (int)node.TriangleOffset;

            for (var i = startIndex; i < startIndex + count; i++)
            {
                var triangle = mesh.Triangles[i];
                var v0 = mesh.VertexPositions[triangle.X];
                var v1 = mesh.VertexPositions[triangle.Y];
                var v2 = mesh.VertexPositions[triangle.Z];

                if (!SweptAABBTriangle(trace, v0, v1, v2, out var hitPoint, out var hitNormal, out var hitDistance))
                {
                    continue;
                }

                // Skip if we're already past a closer hit
                if (hitDistance >= closestHit.Distance)
                {
                    continue;
                }

                // Update closest hit
                closestHit = new(true, hitPoint, hitNormal, hitDistance, i);

                // Early out if we hit at the very start
                if (hitDistance < 1e-6f)
                {
                    return closestHit;
                }
            }
        }

        return closestHit;
    }

    /// <summary>
    /// Performs swept AABB vs triangle collision detection using the Minkowski sum approach.
    /// This effectively expands the triangle by the AABB half extents and performs a ray cast.
    /// </summary>
    private static bool SweptAABBTriangle(
        AABBTraceContext trace,
        Vector3 v0, Vector3 v1, Vector3 v2,
        out Vector3 hitPoint,
        out Vector3 normal,
        out float distance)
    {
        hitPoint = Vector3.Zero;
        normal = Vector3.Zero;
        distance = float.MaxValue;

        // Compute triangle normal and plane
        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var triangleNormal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
        var planeDist = Vector3.Dot(triangleNormal, v0);

        // Calculate the effective radius of the AABB when projected onto the triangle normal
        var radius = Vector3.Dot(trace.HalfExtents, Vector3.Abs(triangleNormal));

        // Check if AABB is already intersecting the plane at start
        var startDist = Vector3.Dot(trace.Origin, triangleNormal) - planeDist;
        var startDistAbs = MathF.Abs(startDist);

        if (startDistAbs <= radius)
        {
            // AABB overlaps plane at start - check if center point is within expanded triangle
            if (PointInExpandedTriangle(trace.Origin, v0, v1, v2, trace.HalfExtents, triangleNormal))
            {
                hitPoint = trace.Origin;
                normal = startDist >= 0 ? triangleNormal : -triangleNormal;
                distance = 0;
                return true;
            }
        }

        // Calculate movement relative to triangle plane
        var moveDir = trace.Direction;
        var moveDot = Vector3.Dot(moveDir, triangleNormal);

        // Moving parallel to plane - no collision possible
        if (MathF.Abs(moveDot) < 1e-6f)
        {
            return false;
        }

        // For swept AABB, we offset the plane by the radius in the direction opposite to movement
        // When moving towards the front (moveDot < 0), offset plane forward by +radius
        // When moving towards the back (moveDot > 0), offset plane backward by -radius
        var signedRadius = moveDot < 0 ? radius : -radius;
        var adjustedPlaneDist = planeDist + signedRadius;

        // Calculate time of impact: when does the AABB center hit the adjusted plane?
        var originDist = Vector3.Dot(trace.Origin, triangleNormal);
        var t = (adjustedPlaneDist - originDist) / moveDot;

        // Check if impact is within sweep range
        if (t < 0 || t > trace.Length)
        {
            return false;
        }

        // Calculate the contact point on the AABB center at time of impact
        var contactCenter = trace.Origin + moveDir * t;

        // Check if the contact center is within the expanded triangle
        // Use the absolute normal for expansion testing (double-sided)
        if (!PointInExpandedTriangle(contactCenter, v0, v1, v2, trace.HalfExtents, triangleNormal))
        {
            return false;
        }

        // Valid hit found - return the normal pointing against movement direction
        hitPoint = contactCenter;
        normal = moveDot < 0 ? triangleNormal : -triangleNormal;
        distance = t;
        return true;
    }

    /// <summary>
    /// Tests if a point is inside a triangle that has been expanded by the AABB half extents.
    /// This implements the Minkowski sum approach for swept AABB collision.
    /// </summary>
    private static bool PointInExpandedTriangle(Vector3 point, Vector3 v0, Vector3 v1, Vector3 v2, Vector3 halfExtents, Vector3 triangleNormal)
    {
        // Project point onto triangle plane
        var toPoint = point - v0;
        var distToPlane = Vector3.Dot(toPoint, triangleNormal);
        var projectedPoint = point - triangleNormal * distToPlane;

        // Check if point is inside the original triangle
        if (PointInTriangle(projectedPoint, v0, v1, v2))
        {
            return true;
        }

        // Get the perpendicular expansion distance for each edge
        // The expansion is the maximum distance the AABB could extend perpendicular to the edge
        var expansion = GetMaxExpansionForTriangle(halfExtents, triangleNormal);

        // Test against expanded edges
        if (PointToEdgeDistance(projectedPoint, v0, v1) <= expansion)
        {
            return true;
        }
        if (PointToEdgeDistance(projectedPoint, v1, v2) <= expansion)
        {
            return true;
        }
        if (PointToEdgeDistance(projectedPoint, v2, v0) <= expansion)
        {
            return true;
        }

        // Test against expanded vertices (sphere check)
        var maxHalfExtent = MathF.Max(halfExtents.X, MathF.Max(halfExtents.Y, halfExtents.Z));
        if (Vector3.Distance(projectedPoint, v0) <= maxHalfExtent)
        {
            return true;
        }
        if (Vector3.Distance(projectedPoint, v1) <= maxHalfExtent)
        {
            return true;
        }
        if (Vector3.Distance(projectedPoint, v2) <= maxHalfExtent)
        {
            return true;
        }

        return false;
    }

    /// <summary>
    /// Calculates the maximum perpendicular expansion for a triangle edge based on AABB half extents.
    /// </summary>
    private static float GetMaxExpansionForTriangle(Vector3 halfExtents, Vector3 normal)
    {
        // Conservative estimate: use the diagonal of the AABB projected perpendicular to normal
        var diagonalLength = halfExtents.Length();
        var normalComponent = Vector3.Dot(halfExtents, Vector3.Abs(normal));

        // Perpendicular component using Pythagorean theorem
        var perpComponent = MathF.Sqrt(MathF.Max(0, diagonalLength * diagonalLength - normalComponent * normalComponent));
        return perpComponent;
    }

    /// <summary>
    /// Calculates the distance from a point to a line segment.
    /// </summary>
    private static float PointToEdgeDistance(Vector3 point, Vector3 edgeStart, Vector3 edgeEnd)
    {
        var edge = edgeEnd - edgeStart;
        var edgeLengthSq = Vector3.Dot(edge, edge);

        if (edgeLengthSq < 1e-6f)
        {
            return Vector3.Distance(point, edgeStart);
        }

        var toPoint = point - edgeStart;
        var t = MathF.Max(0, MathF.Min(1, Vector3.Dot(toPoint, edge) / edgeLengthSq));
        var projection = edgeStart + edge * t;

        return Vector3.Distance(point, projection);
    }

    public static bool PointInTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        const float EPSILON = 1e-6f;

        // Compute vectors
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        // Compute normal to get dominant axis
        var normal = Vector3.Cross(ab, ac);
        var normalLengthSq = Vector3.Dot(normal, normal);

        // Check for degenerate triangle
        if (normalLengthSq < EPSILON * EPSILON)
        {
            return false;
        }

        // Use the largest component of normal to choose projection plane
        var absNormal = Vector3.Abs(normal);
        var dominantAxis = 0;
        if (absNormal.Y > absNormal.X)
        {
            dominantAxis = 1;
        }
        if (absNormal.Z > absNormal[dominantAxis])
        {
            dominantAxis = 2;
        }

        // Project onto the most stable plane
        var u = dominantAxis == 0 ? 1 : 0;
        var v = dominantAxis == 2 ? 1 : 2;

        // Compute area of triangle
        var areaABC = ab[u] * ac[v] - ab[v] * ac[u];
        if (MathF.Abs(areaABC) < EPSILON)
        {
            return false;
        }

        // Compute barycentric coordinates using consistent winding
        var areaABP = ab[u] * ap[v] - ab[v] * ap[u];
        var areaACP = ap[u] * ac[v] - ap[v] * ac[u];

        var w1 = areaABP / areaABC;
        var w2 = areaACP / areaABC;
        var w0 = 1 - w1 - w2;

        // Test if point is inside triangle
        return w0 >= -EPSILON && w1 >= -EPSILON && w2 >= -EPSILON;
    }
}

using System.Linq;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace ValveResourceFormat.Renderer;

public class Rubikon
{
    private const int STACK_SIZE = 64;

    public record PhysicsMeshData(
        string[] InteractAs,
        string[] InteractExclude,
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

    public Node[] HullTree { get; }
    private int[] HullIndices { get; }

    public Rubikon(PhysAggregateData physicsData)
    {
        var worldMeshes = physicsData.Parts[0].Shape.Meshes
            .ToArray();

        Meshes = new PhysicsMeshData[worldMeshes.Length];
        var meshIndex = 0;

        foreach (var mesh in worldMeshes)
        {
            var vertexPositions = mesh.Shape.GetVertices();
            var triangles = mesh.Shape.GetTriangles();
            var physicsTree = mesh.Shape.ParseNodes();

            var collisionAttributes = physicsData.CollisionAttributes[mesh.CollisionAttributeIndex];
            var collisionGroup = collisionAttributes.GetStringProperty("m_CollisionGroupString");

            var interactAs = collisionAttributes.GetArray<string>("m_InteractAsStrings");
            var interactExclude = collisionAttributes.GetArray<string>("m_InteractExcludeStrings");

            Meshes[meshIndex++] = new PhysicsMeshData(interactAs, interactExclude, [.. vertexPositions], [.. triangles], [.. physicsTree]);
        }

        // we want to run player clip traces first because the mesh is much simpler
        Meshes = [.. Meshes.OrderByDescending(m => m.InteractAs.Contains("playerclip"))];

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

        // Build BVH for hulls
        HullIndices = Enumerable.Range(0, Hulls.Length).ToArray();
        HullTree = BuildHullBVH();
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
            if (mesh.InteractAs.Length > 0 && !mesh.InteractAs.Contains("passbullets"))
            {
                continue;
            }

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

    public struct AABBTraceContext
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

    public TraceResult TraceAABB(Vector3 from, Vector3 to, AABB aabb, string collisionName)
    {
        TraceResult closestHit = new();
        var halfExtents = aabb.Size * 0.5f;
        var trace = new AABBTraceContext(from, to, halfExtents);

        // Check against all meshes
        foreach (var mesh in Meshes)
        {
            // player collision rules
            if (collisionName == "player")
            {
                if (mesh.InteractExclude.Contains("player"))
                {
                    continue;
                }

                if (mesh.InteractAs.Length > 0 && !mesh.InteractAs.Contains("playerclip"))
                {
                    continue;
                }
            }

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

        //TODO: Do we want trace handling that guarantees the position is short of an intersection? The math for that is annoying so I will just ignore it for now.

        //if (VertsInsideAABB(new AABB(closestHit.HitPosition - trace.HalfExtents, closestHit.HitPosition + trace.HalfExtents)))
        // {
        //     Debuger.Break();
        // }

        // Check against hulls using BVH
        if (HullTree.Length > 0)
        {
            var hit = AABBTraceHullBVH(trace);
            if (hit.Hit && hit.Distance < closestHit.Distance)
            {
                closestHit = hit;
            }
        }

        return closestHit;
    }

    public bool VertsInsideAABB(AABB aabb)
    {
        foreach (var mesh in Meshes)
        {
            bool hit = MeshVertsInsideAABB(aabb, mesh);

            if (hit)
            {
                return true;
            }
        }
        return false;
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
            var edge0 = hull.HalfEdges[firstEdgeCcw];
            Hull.HalfEdge edge3 = default;

            var edgeIndex = edge0.Next;
            var v0 = hull.VertexPositions[edge0.Origin];

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
                edge3 = hull.HalfEdges[edge2.Next];
            } while (edge3.Origin != edge0.Origin);
        }

        return closestHit;
    }

    private static TraceResult AABBTraceHull(AABBTraceContext trace, PhysicsHullData hull)
    {
        var closestHit = new TraceResult();
        var ray = new RayTraceContext(trace.Origin, trace.End);

        // Expand hull AABB by trace half extents for conservative culling
        if (!RayIntersectsAABB(ray, hull.Min - trace.HalfExtents, hull.Max + trace.HalfExtents))
        {
            return closestHit;
        }

        foreach (var firstEdgeCcw in hull.FaceEdgeIndices)
        {
            var edge0 = hull.HalfEdges[firstEdgeCcw];
            Hull.HalfEdge edge3 = default;

            var edgeIndex = edge0.Next;
            var v0 = hull.VertexPositions[edge0.Origin];

            do
            {
                var edge1 = hull.HalfEdges[edgeIndex];
                var edge2 = hull.HalfEdges[edge1.Next];

                // Just do triangle intersection?
                var v1 = hull.VertexPositions[edge1.Origin];
                var v2 = hull.VertexPositions[edge2.Origin];

                var hit = AABBTraceTriangle(trace, v0, v1, v2);

                if (hit.Hit)
                {
                    if (hit.Distance < closestHit.Distance)
                    {
                        closestHit = hit;
                    }
                }

                edgeIndex = edge1.Next;
                edge3 = hull.HalfEdges[edge2.Next];
            } while (edge3.Origin != edge0.Origin);
        }

        return closestHit;
    }

    private TraceResult AABBTraceHullBVH(AABBTraceContext trace)
    {
        Span<(Node Node, int Index)> stack = stackalloc (Node Node, int Index)[STACK_SIZE];
        var stackCount = 0;
        stack[stackCount++] = (HullTree[0], 0);

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
                stack[stackCount++] = new(HullTree[farId], farId);
                stack[stackCount++] = new(HullTree[nearId], nearId);
                continue;
            }

            // Process hulls in leaf node
            var count = (int)node.ChildOffset;
            var startIndex = (int)node.TriangleOffset;

            for (var i = startIndex; i < startIndex + count; i++)
            {
                var hullIndex = HullIndices[i];
                var hull = Hulls[hullIndex];
                var hit = AABBTraceHull(trace, hull);
                if (hit.Hit && hit.Distance < closestHit.Distance)
                {
                    closestHit = hit;

                    // Early out if we hit something very close to start
                    if (hit.Distance < 1e-4f)
                    {
                        return closestHit;
                    }
                }
            }
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

    private TraceResult AABBTraceMesh(AABBTraceContext trace, PhysicsMeshData mesh)
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

                var hit = AABBTraceTriangle(trace, v0, v1, v2);

                if (!hit.Hit)
                {
                    continue;
                }

                // Skip if we're already past a closer hit
                if (hit.Distance >= closestHit.Distance)
                {
                    continue;
                }

                // Update closest hit
                closestHit = new(true, hit.HitPosition, hit.HitNormal, hit.Distance, i);

                // Early out if we hit at the very start
                if (hit.Distance < 1e-6f)
                {
                    return closestHit;
                }
            }
        }

        return closestHit;
    }

    private static TraceResult AABBTraceTriangle(AABBTraceContext trace, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        var hitPoint = new Vector3(0);
        var hitNormal = new Vector3(0);
        var hitDistance = trace.Length;

        var hasHit = false;
        hasHit = CornerAgainstTri(trace, v0, v1, v2, ref hitPoint, ref hitNormal, ref hitDistance);
        hasHit = hasHit || EdgeAgainstTri(trace, v0, v1, v2, ref hitPoint, ref hitNormal, ref hitDistance);
        hasHit = hasHit || AabbAgainstVert(trace, v0, v1, v2, ref hitPoint, ref hitNormal, ref hitDistance);

        if (!hasHit)
        {
            return new TraceResult();
        }

        return new TraceResult(true, hitPoint, hitNormal, hitDistance, -1);
    }

    private static bool CornerAgainstTri(
        AABBTraceContext trace,
        Vector3 v0, Vector3 v1, Vector3 v2,
        ref Vector3 hitPoint,
        ref Vector3 normal,
        ref float distance)
    {
        //goal: figure out the 1 in 8 corners that can actually hit the tri (its the one whos 3 axis signs is equal to signs(triangle normal) * sign(dot(normal, movedirection))
        //thats the only corner that could collide without having intersection beforehand.

        var edge1 = v1 - v0;
        var edge2 = v2 - v0;
        var triangleNormal = Vector3.Normalize(Vector3.Cross(edge1, edge2));
        Vector3 triNormSign = new Vector3(Math.Sign(triangleNormal.X), Math.Sign(triangleNormal.Y), Math.Sign(triangleNormal.Z));
        Vector3 cornerCoords = trace.Origin + Vector3.Multiply(triNormSign, trace.HalfExtents) * Math.Sign(Vector3.Dot(triangleNormal, trace.Direction));

        //RayTraceContext ray = new RayTraceContext(cornerCoords, trace.Direction);

        RayTraceContext ray = new RayTraceContext(cornerCoords, cornerCoords + trace.Direction * trace.Length * 1);


        //RayTraceContext ray = new RayTraceContext(new Vector3(0), new Vector3(0, 1, 0));

        //v0 = new Vector3(-1, 1, -1);
        //v1 = new Vector3(-1, 1, 2);
        //v2 = new Vector3(2, 1, -1);

        bool DoesHit = RayIntersectsTriangle(ray, v0, v1, v2, out var intersection);

        if (DoesHit)
        {
            normal = triangleNormal;
            distance = intersection.Distance;
            hitPoint = trace.Origin + trace.Direction * distance;
            return true;
        }
        return false;
    }

    private static bool EdgeAgainstTri(
        AABBTraceContext trace,
        Vector3 v0, Vector3 v1, Vector3 v2,
        ref Vector3 hitPoint,
        ref Vector3 normal,
        ref float distance
        )
    {
        //Fundamentally: For each edge on the AABB, we need to do an edge-edge trace against each edge of the triangle.
        //fortunately, we can prefilter that down to 9 edge-edge traces, as only 3 AABB-edges could ever be the first hit for an AABB-trace.

        ReadOnlySpan<Vector3> points = [v0, v1, v2];

        bool hasHit = false;

        for (int edge = 0; edge < 3; edge++)
        {
            Vector3 EdgeStart = points[edge % 3];
            Vector3 EdgeEnd = points[(edge + 1) % 3];

            Vector3 EdgeDirection = EdgeEnd - EdgeStart;

            bool MissesOnAxis = false;

            //Essentially, for the selection of edges, we just look at the world in 2D along each axis once.
            for (int axis = 0; axis < 3 && !MissesOnAxis; axis++)
            {
                //just rotate our edge orientation by 90 deg, flatten and normalize it
                Vector3 hitNormal = new Vector3(0);

                if (Math.Abs(EdgeDirection[(axis + 1) % 3]) < float.Epsilon && Math.Abs(EdgeDirection[(axis + 2) % 3]) < float.Epsilon)
                    continue;

                hitNormal[(axis + 1) % 3] = EdgeDirection[(axis + 2) % 3];
                hitNormal[(axis + 2) % 3] = -EdgeDirection[(axis + 1) % 3];
                hitNormal = Vector3.Normalize(hitNormal) * Math.Sign(-Vector3.Dot(hitNormal, trace.Direction));

                //now to figure out the AABB edge we care about:

                Vector3 AABBEdgeCenter = new Vector3(0);

                AABBEdgeCenter = trace.HalfExtents;

                //example: if normal points up and right, only the bottom left edge can hit
                AABBEdgeCenter[axis] = 0;
                AABBEdgeCenter[(axis + 1) % 3] *= -Math.Sign(hitNormal[(axis + 1) % 3]);
                AABBEdgeCenter[(axis + 2) % 3] *= -Math.Sign(hitNormal[(axis + 2) % 3]);

                AABBEdgeCenter += trace.Origin;

                Vector3 DirToStart = EdgeStart - AABBEdgeCenter;

                //if true, we are moving away from the edge here and that means we can skip all further attemps to intersect it
                if (Vector3.Dot(DirToStart, hitNormal) > 0)
                {
                    //actually this needs commmenting out because we have no check to see if we are already within the box here. If the line is outside the box on this axis, we can't hit it, but we might be alright inside, where this would be wrong.

                    //MissesOnAxis = true;
                    continue;
                }

                //now to figure out the coordinates of where we would land in the extended edge plane

                float Distance = Vector3.Dot(DirToStart, hitNormal) / Vector3.Dot(hitNormal, trace.Direction);

                //same shit here, if we never reach that edge in the first place on any axis, we are not hitting that edge period
                if (Distance > distance)
                {
                    //same as before, this "optimization" breaks shit.
                    //MissesOnAxis = true;
                    continue;
                }

                Vector3 PlaneHitCoord = AABBEdgeCenter + trace.Direction * Distance;

                //I promise this is less clusterfuck than it looks
                int LongestAxisEdgeDir = EdgeDirection[(axis + 1) % 3] > EdgeDirection[(axis + 2) % 3] ? (axis + 1) % 3 : (axis + 2) % 3;

                float diff = PlaneHitCoord[LongestAxisEdgeDir] - EdgeStart[LongestAxisEdgeDir];

                float a = diff / EdgeDirection[LongestAxisEdgeDir];

                //should be obvious that we can't go beyond the edges bounds
                if (a < 0 || a > 1.0f)
                    continue;

                Vector3 NearestOnAxis = EdgeStart + EdgeDirection * diff / EdgeDirection[LongestAxisEdgeDir];

                float AxisDistance = Math.Abs(NearestOnAxis[axis] - PlaneHitCoord[axis]);

                if (AxisDistance < trace.HalfExtents[axis])
                {
                    distance = Distance;
                    normal = hitNormal;
                    hitPoint = trace.Origin + trace.Direction * Distance;
                    hasHit = true;
                }
            }
        }

        return hasHit;
    }

    public static bool MeshVertsInsideAABB(AABB aabb, PhysicsMeshData mesh)
    {
        Span<(Node Node, int Index)> stack = stackalloc (Node Node, int Index)[STACK_SIZE];
        var stackCount = 0;
        stack[stackCount++] = (mesh.PhysicsTree[0], 0);

        var closestHit = new TraceResult();


        while (stackCount > 0)
        {
            var nodeWithIndex = stack[--stackCount];
            var node = nodeWithIndex.Node;

            // Expand node AABB by trace half extents for conservative culling

            if (aabb.Intersects(new AABB(node.Min, node.Max)))
            {
                continue;
            }

            if (node.Type != NodeType.Leaf)
            {
                var leftChild = nodeWithIndex.Index + 1;
                var rightChild = nodeWithIndex.Index + (int)node.ChildOffset;

                //can't be bothered with this, we really don't gaf about the order here, efficiency is for nerds
                var rayIsPositive = true;
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

                var hasHit = aabb.Contains(v0);
                hasHit |= aabb.Contains(v1);
                hasHit |= aabb.Contains(v2);

                if (hasHit)
                {
                    return true;
                }
            }
        }
        return false;
    }

    private static bool AabbAgainstVert(
        AABBTraceContext trace,
        Vector3 v0, Vector3 v1, Vector3 v2,
        ref Vector3 hitPoint,
        ref Vector3 normal,
        ref float distance
        )
    {
        Vector3 TraceOriginMin = trace.Origin - trace.HalfExtents;
        Vector3 TraceOriginMax = trace.Origin + trace.HalfExtents;

        ReadOnlySpan<Vector3> points = [v0, v1, v2];

        var intersects = false;

        for (int i = 0; i < 3; i++)
        {
            var t1 = (TraceOriginMin - points[i]) / -trace.Direction;

            var t2 = (TraceOriginMax - points[i]) / -trace.Direction;

            var tNear = Vector3.Min(t1, t2);

            var tFar = Vector3.Max(t1, t2);

            var tNearMaxIndex = 0;

            for (int j = 1; j < 3; j++)
            {
                if (tNear[j] > tNear[tNearMaxIndex])
                {
                    tNearMaxIndex = j;
                }
            }

            var tNearMax = tNear[tNearMaxIndex];

            var tFarMin = MathF.Min(tFar.X, MathF.Min(tFar.Y, tFar.Z));
            if (tNearMax <= tFarMin && tFarMin > 0 && tNearMax <= trace.Length)
            {
                intersects = true;
                if (tNearMax < distance)
                {
                    distance = tNearMax;
                    normal = new Vector3(0);
                    normal[tNearMaxIndex] = -Math.Sign(trace.Direction[tNearMaxIndex]);
                    hitPoint = distance * trace.Direction + trace.Origin;
                }
            }
        }

        return intersects;
    }

    private Node[] BuildHullBVH()
    {
        if (Hulls.Length == 0)
        {
            return [];
        }

        if (Hulls.Length == 1)
        {
            // Single hull - create a single leaf node
            return [new Node(Hulls[0].Min, Hulls[0].Max, NodeType.Leaf, 1, 0)];
        }

        // Build BVH recursively
        var nodes = new List<Node>();

        BuildHullBVHRecursive(nodes, HullIndices, 0, Hulls.Length);

        return [.. nodes];
    }

    private void BuildHullBVHRecursive(List<Node> nodes, int[] hullIndices, int start, int count)
    {
        var nodeIndex = nodes.Count;
        nodes.Add(default); // Reserve space for this node

        // Calculate bounding box for this range
        var min = Hulls[hullIndices[start]].Min;
        var max = Hulls[hullIndices[start]].Max;

        for (var i = 1; i < count; i++)
        {
            var hull = Hulls[hullIndices[start + i]];
            min = Vector3.Min(min, hull.Min);
            max = Vector3.Max(max, hull.Max);
        }

        // If few enough hulls, make a leaf
        const int MaxHullsPerLeaf = 4;
        if (count <= MaxHullsPerLeaf)
        {
            nodes[nodeIndex] = new Node(
                min,
                max,
                NodeType.Leaf,
                (uint)count,
                (uint)start // Starting index in hullIndices array
            );
            return;
        }

        // Choose split axis based on longest extent
        var extent = max - min;
        var splitAxis = extent.X > extent.Y
            ? (extent.X > extent.Z ? NodeType.SplitX : NodeType.SplitZ)
            : (extent.Y > extent.Z ? NodeType.SplitY : NodeType.SplitZ);

        // Sort hulls along split axis
        var axisIndex = (int)splitAxis;
        Array.Sort(hullIndices, start, count, Comparer<int>.Create((a, b) =>
        {
            var centerA = (Hulls[a].Min[axisIndex] + Hulls[a].Max[axisIndex]) * 0.5f;
            var centerB = (Hulls[b].Min[axisIndex] + Hulls[b].Max[axisIndex]) * 0.5f;
            return centerA.CompareTo(centerB);
        }));

        // Split in the middle
        var leftCount = count / 2;
        var rightCount = count - leftCount;

        // Build left child (immediately after parent)
        var leftChildIndex = nodes.Count;
        BuildHullBVHRecursive(nodes, hullIndices, start, leftCount);

        // Build right child
        var rightChildIndex = nodes.Count;
        BuildHullBVHRecursive(nodes, hullIndices, start + leftCount, rightCount);

        // Update parent node
        var childOffset = (uint)(rightChildIndex - nodeIndex);
        nodes[nodeIndex] = new Node(
            min,
            max,
            splitAxis,
            childOffset,
            0
        );
    }
}

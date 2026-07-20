using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes;
using ValveResourceFormat.Serialization.KeyValues;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace ValveResourceFormat.Renderer;

/// <summary>
/// Ray tracing against Rubikon physics collision shapes including meshes and hulls.
/// </summary>
public class Rubikon
{
    private const int STACK_SIZE = 64;
    private const float Epsilon = 1e-6f;

    /// <summary>
    /// Triangle mesh collision data for ray tracing.
    /// </summary>
    public record PhysicsMeshData(
        string[] InteractAs,
        string[] InteractExclude,
        Vector3[] VertexPositions,
        Triangle[] Triangles,
        Node[] PhysicsTree
    );

    /// <summary>
    /// Convex hull collision data with vertices, edges, and planes.
    /// </summary>
    public record PhysicsHullData(
        Vector3 Min,
        Vector3 Max,
        Vector3[] VertexPositions,
        Hull.HalfEdge[] HalfEdges,
        byte[] FaceEdgeIndices,
        Hull.Plane[] Planes
    );

    /// <summary>Gets the triangle mesh collision shapes available for tracing.</summary>
    public PhysicsMeshData[] Meshes { get; }

    /// <summary>Gets the convex hull collision shapes available for tracing.</summary>
    public PhysicsHullData[] Hulls { get; }

    /// <summary>Gets the BVH acceleration structure built over <see cref="Hulls"/>.</summary>
    public Node[] HullTree { get; }

    private int[] HullIndices { get; }

    /// <summary>Initializes <see cref="Rubikon"/> by parsing the mesh and hull shapes from the first part of the physics aggregate data.</summary>
    /// <param name="physicsData">Source physics aggregate containing shapes and collision attributes.</param>
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
        HullIndices = [.. Enumerable.Range(0, Hulls.Length)];
        HullTree = BuildHullBVH();
    }

    /// <summary>
    /// Ray trace hit result with position, normal, and distance.
    /// </summary>
    public record struct TraceResult(bool Hit, Vector3 HitPosition, Vector3 HitNormal, float Distance, int TriangleIndex)
    {
        /// <summary>Initializes a default <see cref="TraceResult"/> representing a miss at maximum distance.</summary>
        public TraceResult() : this(false, Vector3.Zero, Vector3.UnitZ, float.MaxValue, -1) { }

        /// <summary>
        /// Did we hit something very close to the starting position?
        /// </summary>
        public readonly bool IsMinimalDistance => Distance < 0.00001f;

        /// <summary>
        /// Updates this <see cref="TraceResult"/> if the <paramref name="other"/> is closer. Returns true if updated.
        /// </summary>
        public bool MinimizeWith(TraceResult other)
        {
            if (other.Hit && other.Distance < Distance)
            {
                this = other;
                return true;
            }

            return false;
        }

        /// <summary>Updates this result if <paramref name="other"/> is closer, and returns <see langword="true"/> if the new hit is within the minimal-distance threshold.</summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public bool MinimizeWith_EarlyExit(TraceResult other)
        {
            return MinimizeWith(other) && IsMinimalDistance;
        }
    }

    /// <summary>
    /// Precomputed ray direction data for accelerated ray tracing.
    /// </summary>
    public readonly struct RayTraceContext
    {
        /// <summary>Gets the ray start position.</summary>
        public Vector3 Origin { get; }

        /// <summary>Gets the normalized ray direction.</summary>
        public Vector3 Direction { get; }

        /// <summary>Gets the component-wise reciprocal of <see cref="Direction"/> for slab-method AABB tests.</summary>
        public Vector3 InvDirection { get; }

        /// <summary>Gets the ray length.</summary>
        public float Length { get; }

        /// <summary>Gets the ray end position.</summary>
        public readonly Vector3 EndPosition => Origin + Direction * Length;

        /// <summary>Initializes a new ray trace context from start and end positions.</summary>
        /// <param name="start">Ray start position.</param>
        /// <param name="end">Ray end position.</param>
        public RayTraceContext(Vector3 start, Vector3 end)
        {
            Origin = start;
            Direction = Vector3.Normalize(end - start);
            InvDirection = Vector3.One / Direction;
            Length = Vector3.Distance(start, end);
        }
    }

    /// <summary>Traces a ray against all physics shapes and returns the closest hit.</summary>
    /// <param name="from">Ray start position.</param>
    /// <param name="to">Ray end position.</param>
    /// <returns>The closest <see cref="TraceResult"/>, or an empty result if nothing was hit.</returns>
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
            closestHit.MinimizeWith(hit);
        }

        foreach (var hull in Hulls)
        {
            var hit = RayIntersectsWithHull(ray, hull);
            closestHit.MinimizeWith(hit);
        }

        return closestHit;
    }

    /// <summary>Precomputed sweep data for an axis-aligned box trace.</summary>
    public readonly struct AABBTraceContext
    {
        /// <summary>Gets the start position of the sweep.</summary>
        public Vector3 Origin { get; }

        /// <summary>Gets the end position of the sweep.</summary>
        public Vector3 End { get; }

        /// <summary>Gets the normalized sweep direction.</summary>
        public Vector3 Direction { get; }

        /// <summary>Gets the half-extents of the swept AABB.</summary>
        public Vector3 HalfExtents { get; }

        /// <summary>Gets the total sweep length.</summary>
        public float Length { get; }

        /// <summary>Initializes a new AABB trace context from start/end positions and box half-extents.</summary>
        /// <param name="start">Sweep start position.</param>
        /// <param name="end">Sweep end position.</param>
        /// <param name="halfExtents">Half-extents of the swept box.</param>
        public AABBTraceContext(Vector3 start, Vector3 end, Vector3 halfExtents)
        {
            Origin = start;
            End = end;
            Direction = Vector3.Normalize(end - start);
            HalfExtents = halfExtents;
            Length = Vector3.Distance(start, end);
        }
    }

    /// <summary>Sweeps an axis-aligned bounding box through the physics world and returns the closest hit.</summary>
    /// <param name="from">Sweep start position (center of the AABB).</param>
    /// <param name="to">Sweep end position.</param>
    /// <param name="aabb">Box whose size determines the half-extents of the swept volume.</param>
    /// <param name="collisionName">Collision group name used to filter shapes (e.g. "player").</param>
    /// <returns>The closest <see cref="TraceResult"/>, or an empty result if nothing was hit.</returns>
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
            if (closestHit.MinimizeWith_EarlyExit(hit))
            {
                break;
            }
        }

        if (HullTree.Length > 0)
        {
            var hit = AABBTraceHullBVH(trace);
            closestHit.MinimizeWith(hit);
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

                var hit = AABBTraceTriangle13AxisSat(trace, v0, v1, v2);
                closestHit.MinimizeWith(hit);

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

                if (closestHit.MinimizeWith_EarlyExit(hit))
                {
                    return closestHit;
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
        // Möller-Trumbore ray-triangle intersection algorithm
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

                var hit = AABBTraceTriangle13AxisSat(trace, v0, v1, v2);
                if (closestHit.MinimizeWith_EarlyExit(hit))
                {
                    return closestHit;
                }
            }
        }

        return closestHit;
    }

    private static TraceResult AABBTraceTriangle13AxisSat(AABBTraceContext trace, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        //Needs to exist from the start, as it gets updated while running through the axis.
        var hitNormal = Vector3.Zero;

        ReadOnlySpan<Vector3> triangle = [v0, v1, v2];

        float enter = float.NegativeInfinity, exit = float.PositiveInfinity;

        for (var axis = 0; axis < 13; axis++)
        {
            Vector3 axisVector;
            if (axis == 0)
            {
                axisVector = Vector3.Cross(v1 - v0, v2 - v0);
            }
            else if (axis < 10)
            {
                var localAxisIndex = axis - 1;

                var triangleEdgeIndex = localAxisIndex / 3;
                var boxAxisIndex = localAxisIndex % 3;

                var edge = triangle[(triangleEdgeIndex + 1) % 3] - triangle[triangleEdgeIndex];

                axisVector = edge;
                axisVector[boxAxisIndex] = 0;
                axisVector[(boxAxisIndex + 1) % 3] = -edge[(boxAxisIndex + 2) % 3];
                axisVector[(boxAxisIndex + 2) % 3] = edge[(boxAxisIndex + 1) % 3];

                if (Math.Abs(axisVector[(boxAxisIndex + 1) % 3]) < Epsilon && Math.Abs(axisVector[(boxAxisIndex + 2) % 3]) < Epsilon)
                {
                    continue;
                }
            }
            else
            {
                var localAxisIndex = axis - 10;
                axisVector = Vector3.Zero;
                axisVector[localAxisIndex] = 1;
            }

            axisVector = Vector3.Normalize(axisVector);
            axisVector = Vector3.Dot(trace.Direction, axisVector) > 0 ? axisVector : -axisVector;
            // cosTheta >= 0 because axisVector was flipped toward the ray above.
            // The sweep advances the box projection by cosTheta * Length over the trace.
            var cosTheta = Vector3.Dot(trace.Direction, axisVector);

            var tracedDistanceAlongAxis = cosTheta * trace.Length;
            var boxExtent = Vector3.Dot(Vector3.Abs(axisVector), trace.HalfExtents);

            //project the triangle onto the axis
            float min = float.PositiveInfinity, max = float.NegativeInfinity;

            for (var vertexIdx = 0; vertexIdx < 3; vertexIdx++)
            {
                var projection = Vector3.Dot(triangle[vertexIdx] - trace.Origin, axisVector);

                min = MathF.Min(min, projection - boxExtent);
                max = MathF.Max(max, projection + boxExtent);
            }

            // The axis is (near) perpendicular to the sweep, so the box's projection onto it does
            // not change over the trace and the divisions below would be by ~0. The axis
            // either separates for the whole sweep, or never separates and constrains nothing.
            if (cosTheta <= Epsilon)
            {
                if (min > 0f || max < 0f)
                {
                    return new TraceResult();
                }

                continue;
            }

            //avoids division early
            if (min > tracedDistanceAlongAxis)
            {
                return new TraceResult();
            }

            min /= tracedDistanceAlongAxis;
            max /= tracedDistanceAlongAxis;

            if (min > enter)
            {
                hitNormal = -axisVector;
                enter = min;
            }

            exit = MathF.Min(exit, max);

            if (enter > exit || exit <= 0)
            {
                return new TraceResult();
            }
        }

        if (enter > 1.0f)
        {
            return new TraceResult();
        }

        var hitDistance = Math.Max(enter * trace.Length, 0);
        var hitPoint = trace.Origin + trace.Direction * hitDistance;

        return new TraceResult(true, hitPoint, hitNormal, hitDistance, -1);
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

        BuildHullBVHRecursive(nodes, HullIndices, 0, Hulls.Length, 0);

        return [.. nodes];
    }

    private void BuildHullBVHRecursive(List<Node> nodes, int[] hullIndices, int start, int count, int depth)
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

        // If few enough hulls or max depth reached, make a leaf
        const int MaxHullsPerLeaf = 4;
        if (count <= MaxHullsPerLeaf || depth >= STACK_SIZE)
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
        BuildHullBVHRecursive(nodes, hullIndices, start, leftCount, depth + 1);

        // Build right child
        var rightChildIndex = nodes.Count;
        BuildHullBVHRecursive(nodes, hullIndices, start + leftCount, rightCount, depth + 1);

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

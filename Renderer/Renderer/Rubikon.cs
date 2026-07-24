using System.Linq;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using ValveKeyValue;
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
    /// <summary>
    /// Degenerate-geometry cutoff used throughout the tracer. Sweeps shorter than this are
    /// rejected as invalid rays (reported as a miss with <see cref="TraceResult.IsValid"/>
    /// unset), so callers that apply movement must not treat sub-epsilon deltas as traced.
    /// </summary>
    public const float Epsilon = 1e-6f;

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
        string[] InteractAs,
        string[] InteractExclude,
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
        if (physicsData.Parts.Length == 0)
        {
            Meshes = [];
            Hulls = [];
            HullIndices = [];
            HullTree = [];
            return;
        }

        var worldMeshes = physicsData.Parts[0].Shape.Meshes
            .ToArray();

        Meshes = new PhysicsMeshData[worldMeshes.Length];
        var meshIndex = 0;

        foreach (var mesh in worldMeshes)
        {
            var vertexPositions = mesh.Shape.GetVertices();
            var triangles = mesh.Shape.GetTriangles();
            var physicsTree = mesh.Shape.ParseNodes();

            var (interactAs, interactExclude) = GetInteractStrings(physicsData.CollisionAttributes[mesh.CollisionAttributeIndex]);

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

            var (interactAs, interactExclude) = GetInteractStrings(physicsData.CollisionAttributes[hullDesc.CollisionAttributeIndex]);

            Hulls[hullIndex++] = new PhysicsHullData(
                interactAs, interactExclude,
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
    /// Older assets carry their tags in m_PhysicsTagStrings instead of m_InteractAsStrings,
    /// and GetArray returns null for absent keys, so both interact arrays need a fallback.
    /// </summary>
    private static (string[] InteractAs, string[] InteractExclude) GetInteractStrings(KVObject collisionAttributes)
    {
        var interactAs = collisionAttributes.GetArray<string>("m_InteractAsStrings")
            ?? collisionAttributes.GetArray<string>("m_PhysicsTagStrings")
            ?? [];
        var interactExclude = collisionAttributes.GetArray<string>("m_InteractExcludeStrings") ?? [];

        return (interactAs, interactExclude);
    }

    /// <summary>
    /// Ray trace hit result with position, normal, and distance.
    /// </summary>
    public record struct TraceResult(bool Hit, Vector3 HitPosition, Vector3 HitNormal, float Distance, int TriangleIndex)
    {
        /// <summary>Initializes a default <see cref="TraceResult"/> representing a miss at maximum distance.</summary>
        public TraceResult() : this(false, Vector3.Zero, Vector3.UnitZ, float.MaxValue, -1) { }

        private bool fired = true;

        /// <summary>
        /// Gets or sets a value indicating whether the trace actually fired. <see langword="false"/>
        /// when the sweep was rejected (sub-<see cref="Epsilon"/> length): the result is reported
        /// as a miss but carries no information about the geometry, so callers must not treat
        /// the sweep as validated.
        /// </summary>
        public bool IsValid { readonly get => fired; set => fired = value; }

        /// <summary>
        /// Gets or sets a value indicating whether the swept shape already overlapped geometry
        /// at the start position. When set, <see cref="Distance"/> is 0 and the reported normal
        /// belongs to one arbitrary overlapping triangle.
        /// </summary>
        public bool StartSolid { get; set; }

        /// <summary>
        /// Did we hit something very close to the starting position?
        /// </summary>
        public readonly bool IsMinimalDistance => Distance < 0.00001f;

        /// <summary>
        /// Gets or sets the point of contact on the hit surface. For swept box traces this is
        /// the closest point on the hit triangle to the box center at the time of impact,
        /// while <see cref="HitPosition"/> is the center of the swept shape itself.
        /// </summary>
        public Vector3 ContactPoint { get; set; }

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

        /// <summary>
        /// Whether shape scanning can stop at this result: the hit is as close as it can get,
        /// and if the trace is probing for start-solid, only an actual overlap qualifies -
        /// an exact-touch zero-distance hit must not mask an embedded shape tested later.
        /// </summary>
        [MethodImpl(MethodImplOptions.AggressiveInlining)]
        public readonly bool StopsScanning(bool detectStartSolid)
        {
            return IsMinimalDistance && (!detectStartSolid || StartSolid);
        }
    }

    /// <summary>
    /// Precomputed ray direction data for accelerated ray tracing.
    /// </summary>
    public readonly record struct RayTraceContext
    {
        /// <summary>Gets the ray start position.</summary>
        public Vector3 Origin { get; init; }

        /// <summary>Gets the normalized ray direction.</summary>
        public Vector3 Direction { get; init; }

        /// <summary>Gets the component-wise reciprocal of <see cref="Direction"/> for slab-method AABB tests.</summary>
        public Vector3 InvDirection { get; init; }

        /// <summary>Gets the ray length.</summary>
        public float Length { get; init; }

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

    private static bool IsInvalidRay(Vector3 from, Vector3 to)
    {
        return Vector3.DistanceSquared(from, to) < Epsilon * Epsilon;
    }

    /// <summary>Traces a ray against all physics shapes and returns the closest hit.</summary>
    /// <param name="from">Ray start position.</param>
    /// <param name="to">Ray end position.</param>
    /// <returns>The closest <see cref="TraceResult"/>, or an empty result if nothing was hit.</returns>
    public TraceResult TraceRay(Vector3 from, Vector3 to)
    {
        TraceResult closestHit = new();

        if (IsInvalidRay(from, to))
        {
            closestHit.IsValid = false;
            return closestHit;
        }

        RayTraceContext ray = new(from, to);

        foreach (var mesh in Meshes)
        {
            if (mesh.InteractAs.Length > 0 && !ContainsString(mesh.InteractAs, "passbullets"))
            {
                continue;
            }

            RayIntersectsWithMesh(ray, mesh, ref closestHit);
        }

        foreach (var hull in Hulls)
        {
            // Invisible clip geometry should not block picking, matching the mesh filter above
            if (ContainsString(hull.InteractAs, "playerclip"))
            {
                continue;
            }

            RayIntersectsWithHull(ray, hull, ref closestHit);
        }

        return closestHit;
    }

    /// <summary>Optional behaviors of an axis-aligned box trace.</summary>
    [Flags]
    public enum TraceOptions
    {
        /// <summary>No optional behaviors.</summary>
        None = 0,

        /// <summary>Test triangles for overlap at the start position (reported as <see cref="TraceResult.StartSolid"/>).</summary>
        DetectStartSolid = 1 << 0,

        /// <summary>Also compute <see cref="TraceResult.ContactPoint"/> on hits.</summary>
        ComputeContactPoint = 1 << 1,
    }

    /// <summary>Precomputed sweep data for an axis-aligned box trace.</summary>
    public readonly struct AABBTraceContext
    {
        /// <summary>Gets the sweep center line as a precomputed ray.</summary>
        public RayTraceContext Ray { get; }

        /// <summary>Gets the half-extents of the swept AABB.</summary>
        public Vector3 HalfExtents { get; }

        /// <summary>Gets the optional behaviors of this trace.</summary>
        public TraceOptions Options { get; }

        /// <summary>Gets a value indicating whether triangles are tested for overlap at the
        /// start position (reported as <see cref="TraceResult.StartSolid"/>).</summary>
        public bool DetectStartSolid => (Options & TraceOptions.DetectStartSolid) != 0;

        /// <summary>Gets a value indicating whether hits also compute <see cref="TraceResult.ContactPoint"/>.</summary>
        public bool ComputeContactPoint => (Options & TraceOptions.ComputeContactPoint) != 0;

        /// <summary>Gets the start position of the sweep.</summary>
        public Vector3 Origin => Ray.Origin;

        /// <summary>Gets the normalized sweep direction.</summary>
        public Vector3 Direction => Ray.Direction;

        /// <summary>Gets the total sweep length.</summary>
        public float Length => Ray.Length;

        /// <summary>Initializes a new AABB trace context from start/end positions and box half-extents.</summary>
        /// <param name="start">Sweep start position.</param>
        /// <param name="end">Sweep end position.</param>
        /// <param name="halfExtents">Half-extents of the swept box.</param>
        /// <param name="options">Optional behaviors of the trace.</param>
        public AABBTraceContext(Vector3 start, Vector3 end, Vector3 halfExtents, TraceOptions options = TraceOptions.None)
        {
            Ray = new RayTraceContext(start, end);
            HalfExtents = halfExtents;
            Options = options;
        }
    }

    /// <summary>Sweeps an axis-aligned bounding box through the physics world and returns the closest hit.</summary>
    /// <param name="from">Sweep start position (center of the AABB).</param>
    /// <param name="to">Sweep end position.</param>
    /// <param name="aabb">Box whose size determines the half-extents of the swept volume.</param>
    /// <param name="collisionName">Collision group name used to filter shapes (e.g. "player").</param>
    /// <param name="detectStartSolid">Whether to also test for overlap at the start position (see <see cref="TraceResult.StartSolid"/>).</param>
    /// <param name="computeContactPoint">Whether hits also compute <see cref="TraceResult.ContactPoint"/>.</param>
    /// <returns>The closest <see cref="TraceResult"/>, or an empty result if nothing was hit.</returns>
    public TraceResult TraceAABB(Vector3 from, Vector3 to, AABB aabb, string collisionName, bool detectStartSolid = false, bool computeContactPoint = false)
        => TraceAABB(from, to, aabb.Size * 0.5f, collisionName, detectStartSolid, computeContactPoint);

    /// <summary>Sweeps a box given by its half-extents from <paramref name="from"/> to <paramref name="to"/> and returns the closest hit.</summary>
    /// <param name="from">Sweep start position (center of the box).</param>
    /// <param name="to">Sweep end position.</param>
    /// <param name="halfExtents">Half-extents of the swept box.</param>
    /// <param name="collisionName">Collision interaction name used to filter shapes.</param>
    /// <param name="detectStartSolid">Whether to report overlaps at the start position as start-solid hits.</param>
    /// <param name="computeContactPoint">Whether hits also compute <see cref="TraceResult.ContactPoint"/>.</param>
    /// <returns>The closest <see cref="TraceResult"/>, or an empty result if nothing was hit.</returns>
    public TraceResult TraceAABB(Vector3 from, Vector3 to, Vector3 halfExtents, string collisionName, bool detectStartSolid = false, bool computeContactPoint = false)
    {
        TraceResult closestHit = new();

        if (IsInvalidRay(from, to))
        {
            closestHit.IsValid = false;
            return closestHit;
        }

        var options = (detectStartSolid ? TraceOptions.DetectStartSolid : TraceOptions.None)
            | (computeContactPoint ? TraceOptions.ComputeContactPoint : TraceOptions.None);
        var trace = new AABBTraceContext(from, to, halfExtents, options);

        // Check against all meshes
        foreach (var mesh in Meshes)
        {
            if (SkipsCollision(collisionName, mesh.InteractAs, mesh.InteractExclude))
            {
                continue;
            }

            AABBTraceMesh(trace, mesh, ref closestHit);
            if (closestHit.StopsScanning(trace.DetectStartSolid))
            {
                break;
            }
        }

        if (HullTree.Length > 0)
        {
            AABBTraceHullBVH(trace, collisionName, ref closestHit);
        }

        return closestHit;
    }

    /// <summary>Tests a box at rest for overlap against the physics shapes it collides with, without sweeping.
    /// Unlike <see cref="TraceAABB(Vector3, Vector3, Vector3, string, bool, bool)"/> this accepts
    /// a pure position, so it can answer "am I stuck here" without a probe direction.</summary>
    /// <param name="center">Center of the box.</param>
    /// <param name="halfExtents">Half-extents of the box.</param>
    /// <param name="collisionName">Collision interaction name used to filter shapes.</param>
    /// <returns><see langword="true"/> if any physics triangle overlaps the box.</returns>
    public bool CheckOverlap(Vector3 center, Vector3 halfExtents, string collisionName)
    {
        foreach (var mesh in Meshes)
        {
            if (SkipsCollision(collisionName, mesh.InteractAs, mesh.InteractExclude))
            {
                continue;
            }

            var meshQuery = new OverlapMeshQuery(center, halfExtents, mesh);
            TraverseBvh(mesh.PhysicsTree, ref meshQuery);

            if (meshQuery.Overlaps)
            {
                return true;
            }
        }

        if (HullTree.Length > 0)
        {
            var hullQuery = new OverlapHullsQuery(center, halfExtents, collisionName, Hulls, HullIndices);
            TraverseBvh(HullTree, ref hullQuery);

            if (hullQuery.Overlaps)
            {
                return true;
            }
        }

        return false;
    }

    /// <summary>A volume query runnable against a BVH by <see cref="TraverseBvh"/>. Implemented by
    /// structs so the constrained calls devirtualize; mutable query state lives in the struct.</summary>
    private interface IBvhQuery
    {
        /// <summary>Whether the query volume reaches this node's bounds; <see langword="false"/> culls the subtree.</summary>
        bool IntersectsNode(in Node node);

        /// <summary>Which child to descend into first when the node splits on the given axis.</summary>
        bool DescendLeftFirst(int splitAxis);

        /// <summary>Visits the leaf items [<paramref name="start"/>, <paramref name="start"/> + <paramref name="count"/>);
        /// returns <see langword="true"/> to stop the traversal.</summary>
        bool VisitLeaf(int start, int count);
    }

    private static void TraverseBvh<TQuery>(Node[] nodes, ref TQuery query) where TQuery : struct, IBvhQuery
    {
        Span<(Node Node, int Index)> stack = stackalloc (Node Node, int Index)[STACK_SIZE];
        var stackCount = 0;
        stack[stackCount++] = (nodes[0], 0);

        while (stackCount > 0)
        {
            var (node, index) = stack[--stackCount];

            if (!query.IntersectsNode(in node))
            {
                continue;
            }

            if (node.Type != NodeType.Leaf)
            {
                var leftChild = index + 1;
                var rightChild = index + (int)node.ChildOffset;

                var (nearId, farId) = query.DescendLeftFirst((int)node.Type)
                    ? (leftChild, rightChild)
                    : (rightChild, leftChild);

                // Push far node first so near node is processed first (stack is LIFO)
                stack[stackCount++] = (nodes[farId], farId);
                stack[stackCount++] = (nodes[nearId], nearId);
                continue;
            }

            if (query.VisitLeaf((int)node.TriangleOffset, (int)node.ChildOffset))
            {
                return;
            }
        }
    }

    private struct OverlapMeshQuery(Vector3 center, Vector3 halfExtents, PhysicsMeshData mesh) : IBvhQuery
    {
        public bool Overlaps;

        public readonly bool IntersectsNode(in Node node) => BoxIntersectsAABB(center, halfExtents, node.Min, node.Max);

        public readonly bool DescendLeftFirst(int splitAxis) => true;

        public bool VisitLeaf(int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var triangle = mesh.Triangles[i];
                var v0 = mesh.VertexPositions[triangle.X];
                var v1 = mesh.VertexPositions[triangle.Y];
                var v2 = mesh.VertexPositions[triangle.Z];

                if (TriangleOverlaps(center, halfExtents, v0, v1, v2))
                {
                    Overlaps = true;
                    return true;
                }
            }

            return false;
        }
    }

    private struct OverlapHullsQuery(Vector3 center, Vector3 halfExtents, string collisionName, PhysicsHullData[] hulls, int[] hullIndices) : IBvhQuery
    {
        public bool Overlaps;

        public readonly bool IntersectsNode(in Node node) => BoxIntersectsAABB(center, halfExtents, node.Min, node.Max);

        public readonly bool DescendLeftFirst(int splitAxis) => true;

        public bool VisitLeaf(int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var hull = hulls[hullIndices[i]];

                if (SkipsCollision(collisionName, hull.InteractAs, hull.InteractExclude))
                {
                    continue;
                }

                if (!BoxIntersectsAABB(center, halfExtents, hull.Min, hull.Max))
                {
                    continue;
                }

                var triangles = new HullTriangleEnumerator(hull);

                while (triangles.MoveNext(out var v0, out var v1, out var v2))
                {
                    if (TriangleOverlaps(center, halfExtents, v0, v1, v2))
                    {
                        Overlaps = true;
                        return true;
                    }
                }
            }

            return false;
        }
    }

    /// <summary>
    /// Generates the 13 SAT axes for a box-triangle test: the triangle face normal (0), the
    /// 9 cross products of triangle edges with the box axes (1-9), and the 3 box axes (10-12).
    /// Returns <see langword="false"/> for axes that constrain nothing and should be skipped.
    /// </summary>
    /// <param name="axis">Axis index from 0 to 12.</param>
    /// <param name="triangle">The three triangle vertices.</param>
    /// <param name="skipDegenerateFace">Whether to also skip the face axis of a zero-area triangle
    /// (its edge and box axes still apply).</param>
    /// <param name="axisVector">The generated, unnormalized axis.</param>
    private static bool TryGetSatAxis(int axis, ReadOnlySpan<Vector3> triangle, bool skipDegenerateFace, out Vector3 axisVector)
    {
        if (axis == 0)
        {
            axisVector = Vector3.Cross(triangle[1] - triangle[0], triangle[2] - triangle[0]);

            if (skipDegenerateFace && axisVector.LengthSquared() < Epsilon * Epsilon)
            {
                return false;
            }
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
                return false;
            }
        }
        else
        {
            var localAxisIndex = axis - 10;
            axisVector = Vector3.Zero;
            axisVector[localAxisIndex] = 1;
        }

        return true;
    }

    /// <summary>
    /// 13-axis SAT overlap test between a box at rest and a triangle: the face normal,
    /// 9 edge cross products, and 3 box axes.
    /// </summary>
    private static bool TriangleOverlaps(Vector3 center, Vector3 halfExtents, Vector3 v0, Vector3 v1, Vector3 v2)
    {
        ReadOnlySpan<Vector3> triangle = [v0, v1, v2];

        for (var axis = 0; axis < 13; axis++)
        {
            if (!TryGetSatAxis(axis, triangle, skipDegenerateFace: true, out var axisVector))
            {
                continue;
            }

            // The separation test is scale-invariant: boxExtent and the projections both scale
            // with the axis length, so the axis does not need to be normalized
            var boxExtent = Vector3.Dot(Vector3.Abs(axisVector), halfExtents);

            // Project the triangle onto the axis, relative to the box center
            float min = float.PositiveInfinity, max = float.NegativeInfinity;

            for (var vertexIdx = 0; vertexIdx < 3; vertexIdx++)
            {
                var projection = Vector3.Dot(triangle[vertexIdx] - center, axisVector);
                min = MathF.Min(min, projection);
                max = MathF.Max(max, projection);
            }

            if (min > boxExtent || max < -boxExtent)
            {
                return false;
            }
        }

        return true;
    }

    /// <summary>Enumerates a hull's faces as triangle fans anchored at each face's first vertex.</summary>
    private struct HullTriangleEnumerator(PhysicsHullData hull)
    {
        private int faceIndex;
        private int edgeIndex = -1;
        private int firstOrigin;
        private Vector3 fanAnchor;

        public bool MoveNext(out Vector3 v0, out Vector3 v1, out Vector3 v2)
        {
            if (edgeIndex < 0)
            {
                if (faceIndex >= hull.FaceEdgeIndices.Length)
                {
                    v0 = v1 = v2 = default;
                    return false;
                }

                var edge0 = hull.HalfEdges[hull.FaceEdgeIndices[faceIndex++]];
                firstOrigin = edge0.Origin;
                fanAnchor = hull.VertexPositions[edge0.Origin];
                edgeIndex = edge0.Next;
            }

            var edge1 = hull.HalfEdges[edgeIndex];
            var edge2 = hull.HalfEdges[edge1.Next];

            v0 = fanAnchor;
            v1 = hull.VertexPositions[edge1.Origin];
            v2 = hull.VertexPositions[edge2.Origin];

            edgeIndex = edge1.Next;

            // Face done once the fan wraps back around; start the next face on the following call
            if (hull.HalfEdges[edge2.Next].Origin == firstOrigin)
            {
                edgeIndex = -1;
            }

            return true;
        }
    }

    private static bool BoxIntersectsAABB(Vector3 center, Vector3 halfExtents, Vector3 min, Vector3 max)
    {
        return center.X >= min.X - halfExtents.X && center.X <= max.X + halfExtents.X
            && center.Y >= min.Y - halfExtents.Y && center.Y <= max.Y + halfExtents.Y
            && center.Z >= min.Z - halfExtents.Z && center.Z <= max.Z + halfExtents.Z;
    }

    /// <summary>
    /// Player collision rules: player traces never collide with shapes that exclude the player,
    /// and only collide with default geometry, player clips, and passbullets shapes.
    /// </summary>
    private static bool SkipsCollision(string collisionName, string[] interactAs, string[] interactExclude)
    {
        if (collisionName != "player")
        {
            return false;
        }

        if (ContainsString(interactExclude, "player"))
        {
            return true;
        }

        return interactAs.Length > 0
            && !ContainsString(interactAs, "playerclip")
            && !ContainsString(interactAs, "passbullets")
            && !ContainsString(interactAs, "window");
    }

    private static bool ContainsString(string[] values, string value)
    {
        foreach (var entry in values)
        {
            if (entry == value)
            {
                return true;
            }
        }

        return false;
    }

    private static void RayIntersectsWithHull(RayTraceContext ray, PhysicsHullData hull, ref TraceResult closestHit)
    {
        // Skip hulls that cannot contain a hit closer than the best one found so far
        if (!RayIntersectsAABB(ray, hull.Min, hull.Max, out var entryDistance) || entryDistance > closestHit.Distance)
        {
            return;
        }

        var triangles = new HullTriangleEnumerator(hull);

        while (triangles.MoveNext(out var v0, out var v1, out var v2))
        {
            // Update if this is the closest hit
            if (RayIntersectsTriangle(ray, v0, v1, v2, out var intersection) && intersection.Distance < closestHit.Distance)
            {
                closestHit = new(true, ray.Origin + ray.Direction * intersection.Distance, intersection.Normal, intersection.Distance, -1);
            }
        }
    }

    private static void AABBTraceHull(AABBTraceContext trace, PhysicsHullData hull, ref TraceResult closestHit)
    {
        // Expand hull AABB by trace half extents for conservative culling, and skip
        // hulls that cannot contain a hit closer than the best one found so far
        if (!RayIntersectsAABB(trace.Ray, hull.Min - trace.HalfExtents, hull.Max + trace.HalfExtents, out var entryDistance) || entryDistance > closestHit.Distance)
        {
            return;
        }

        var triangles = new HullTriangleEnumerator(hull);

        while (triangles.MoveNext(out var v0, out var v1, out var v2))
        {
            AABBTraceTriangle13AxisSat(trace, v0, v1, v2, ref closestHit);

            if (closestHit.StopsScanning(trace.DetectStartSolid))
            {
                return;
            }
        }
    }

    private void AABBTraceHullBVH(AABBTraceContext trace, string collisionName, ref TraceResult closestHit)
    {
        var query = new SweepHullsQuery(trace, collisionName, Hulls, HullIndices) { ClosestHit = closestHit };
        TraverseBvh(HullTree, ref query);
        closestHit = query.ClosestHit;
    }

    private struct SweepHullsQuery(AABBTraceContext trace, string collisionName, PhysicsHullData[] hulls, int[] hullIndices) : IBvhQuery
    {
        public TraceResult ClosestHit;

        // Expand node AABB by trace half extents for conservative culling, and skip
        // nodes that cannot contain a hit closer than the best one found so far
        public readonly bool IntersectsNode(in Node node)
            => RayIntersectsAABB(trace.Ray, node.Min - trace.HalfExtents, node.Max + trace.HalfExtents, out var entryDistance) && entryDistance <= ClosestHit.Distance;

        // Traverse the child nearest along the sweep first
        public readonly bool DescendLeftFirst(int splitAxis) => trace.Direction[splitAxis] >= 0;

        public bool VisitLeaf(int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var hull = hulls[hullIndices[i]];

                if (SkipsCollision(collisionName, hull.InteractAs, hull.InteractExclude))
                {
                    continue;
                }

                AABBTraceHull(trace, hull, ref ClosestHit);

                if (ClosestHit.StopsScanning(trace.DetectStartSolid))
                {
                    return true;
                }
            }

            return false;
        }
    }

    private static void RayIntersectsWithMesh(RayTraceContext ray, PhysicsMeshData mesh, ref TraceResult closestHit)
    {
        var query = new RayMeshQuery(ray, mesh) { ClosestHit = closestHit };
        TraverseBvh(mesh.PhysicsTree, ref query);
        closestHit = query.ClosestHit;
    }

    private struct RayMeshQuery(RayTraceContext ray, PhysicsMeshData mesh) : IBvhQuery
    {
        public TraceResult ClosestHit;

        // Skip nodes that cannot contain a hit closer than the best one found so far
        public readonly bool IntersectsNode(in Node node)
            => RayIntersectsAABB(ray, node.Min, node.Max, out var entryDistance) && entryDistance <= ClosestHit.Distance;

        // Traverse the child nearest along the ray first
        public readonly bool DescendLeftFirst(int splitAxis) => ray.Direction[splitAxis] >= 0;

        public bool VisitLeaf(int start, int count)
        {
            for (var i = start; i < start + count; i++)
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
                if (intersection.Distance < ClosestHit.Distance)
                {
                    ClosestHit = new(true, ray.Origin + ray.Direction * intersection.Distance, intersection.Normal, intersection.Distance, i);
                }
            }

            return false;
        }
    }

    private static bool RayIntersectsAABB(RayTraceContext ray, Vector3 min, Vector3 max, out float entryDistance)
    {
        // Calculate intersection with AABB using slab method
        var t1 = (min - ray.Origin) * ray.InvDirection;
        var t2 = (max - ray.Origin) * ray.InvDirection;

        var tNear = Vector3.Min(t1, t2);
        var tFar = Vector3.Max(t1, t2);

        var tNearMax = MathF.Max(tNear.X, MathF.Max(tNear.Y, tNear.Z));
        var tFarMin = MathF.Min(tFar.X, MathF.Min(tFar.Y, tFar.Z));

        // Negative when the ray starts inside the box
        entryDistance = tNearMax;

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
        if (Math.Abs(a) < Epsilon)
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

    private static void AABBTraceMesh(AABBTraceContext trace, PhysicsMeshData mesh, ref TraceResult closestHit)
    {
        var query = new SweepMeshQuery(trace, mesh) { ClosestHit = closestHit };
        TraverseBvh(mesh.PhysicsTree, ref query);
        closestHit = query.ClosestHit;
    }

    private struct SweepMeshQuery(AABBTraceContext trace, PhysicsMeshData mesh) : IBvhQuery
    {
        public TraceResult ClosestHit;

        // Expand node AABB by trace half extents for conservative culling, and skip
        // nodes that cannot contain a hit closer than the best one found so far
        public readonly bool IntersectsNode(in Node node)
            => RayIntersectsAABB(trace.Ray, node.Min - trace.HalfExtents, node.Max + trace.HalfExtents, out var entryDistance) && entryDistance <= ClosestHit.Distance;

        // Traverse the child nearest along the sweep first
        public readonly bool DescendLeftFirst(int splitAxis) => trace.Direction[splitAxis] >= 0;

        public bool VisitLeaf(int start, int count)
        {
            for (var i = start; i < start + count; i++)
            {
                var triangle = mesh.Triangles[i];
                var v0 = mesh.VertexPositions[triangle.X];
                var v1 = mesh.VertexPositions[triangle.Y];
                var v2 = mesh.VertexPositions[triangle.Z];

                AABBTraceTriangle13AxisSat(trace, v0, v1, v2, ref ClosestHit);

                if (ClosestHit.StopsScanning(trace.DetectStartSolid))
                {
                    return true;
                }
            }

            return false;
        }
    }


    // SAT axes entering within this distance of each other count as simultaneous contacts;
    // used to prefer the face normal over an edge/vertex axis
    private const float NormalWeldTolerance = 1e-3f;

    private static void AABBTraceTriangle13AxisSat(AABBTraceContext trace, Vector3 v0, Vector3 v1, Vector3 v2, ref TraceResult closestHit)
    {
        //Needs to exist from the start, as it gets updated while running through the axis.
        var hitNormal = Vector3.Zero;

        ReadOnlySpan<Vector3> triangle = [v0, v1, v2];

        float enter = float.NegativeInfinity, exit = float.PositiveInfinity;

        // Face axis entry, kept for normal welding below
        var faceEnter = float.NegativeInfinity;
        var faceNormal = Vector3.Zero;

        for (var axis = 0; axis < 13; axis++)
        {
            if (!TryGetSatAxis(axis, triangle, skipDegenerateFace: false, out var axisVector))
            {
                continue;
            }

            // Degenerate (zero-area) triangle: there is no surface to hit, and normalizing
            // the zero cross product would poison the interval tests with NaN
            if (axis == 0 && axisVector.LengthSquared() < Epsilon * Epsilon)
            {
                return;
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
                var vertex = triangle[vertexIdx];
                var relativeVertexPos = vertex - trace.Origin;
                var projection = Vector3.Dot(relativeVertexPos, axisVector);

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
                    return;
                }

                continue;
            }

            //avoids division early
            if (min > tracedDistanceAlongAxis)
                return;

            min /= tracedDistanceAlongAxis;
            max /= tracedDistanceAlongAxis;

            if (min > enter)
            {
                hitNormal = -axisVector;
                enter = min;
            }
            exit = MathF.Min(exit, max);

            if (axis == 0)
            {
                faceEnter = min;
                faceNormal = -axisVector;
            }

            if (enter > exit || exit <= 0)
                return;
        }
        if (enter > 1.0f)
            return;

        // Normal welding: an edge/vertex entry whose face plane was crossed at essentially the
        // same instant is a phantom internal-edge contact (the "rampbug"); report the face
        // normal instead. Genuine exterior edges cross the face plane much earlier, or never.
        if (faceEnter > float.NegativeInfinity && (enter - faceEnter) * trace.Length <= NormalWeldTolerance)
        {
            hitNormal = faceNormal;
        }

        // Already overlapping this triangle at the start position - nothing can be closer,
        // so report start-solid and let the callers early-exit
        if (trace.DetectStartSolid && enter < 0 && exit >= 0)
        {
            var startNormal = Vector3.Cross(v1 - v0, v2 - v0);
            var startNormalLength = startNormal.Length();
            startNormal = startNormalLength > Epsilon ? startNormal / startNormalLength : Vector3.UnitZ;

            if (Vector3.Dot(startNormal, trace.Direction) > 0)
            {
                startNormal = -startNormal;
            }

            closestHit = new TraceResult(true, trace.Origin, startNormal, 0f, -1)
            {
                StartSolid = true,
                ContactPoint = trace.ComputeContactPoint ? ClosestPointOnTriangle(trace.Origin, v0, v1, v2) : trace.Origin,
            };
            return;
        }

        var hitDistance = Math.Max(enter * trace.Length, 0);

        var hitPoint = trace.Origin + trace.Direction * hitDistance;

        if (hitDistance < closestHit.Distance)
        {
            closestHit = new TraceResult(true, hitPoint, hitNormal, hitDistance, -1)
            {
                ContactPoint = trace.ComputeContactPoint ? ClosestPointOnTriangle(hitPoint, v0, v1, v2) : hitPoint,
            };
        }
    }

    private static Vector3 ClosestPointOnTriangle(Vector3 p, Vector3 a, Vector3 b, Vector3 c)
    {
        var ab = b - a;
        var ac = c - a;
        var ap = p - a;

        var d1 = Vector3.Dot(ab, ap);
        var d2 = Vector3.Dot(ac, ap);
        if (d1 <= 0 && d2 <= 0)
        {
            return a;
        }

        var bp = p - b;
        var d3 = Vector3.Dot(ab, bp);
        var d4 = Vector3.Dot(ac, bp);
        if (d3 >= 0 && d4 <= d3)
        {
            return b;
        }

        var vc = d1 * d4 - d3 * d2;
        if (vc <= 0 && d1 >= 0 && d3 <= 0)
        {
            return a + ab * (d1 / (d1 - d3));
        }

        var cp = p - c;
        var d5 = Vector3.Dot(ab, cp);
        var d6 = Vector3.Dot(ac, cp);
        if (d6 >= 0 && d5 <= d6)
        {
            return c;
        }

        var vb = d5 * d2 - d1 * d6;
        if (vb <= 0 && d2 >= 0 && d6 <= 0)
        {
            return a + ac * (d2 / (d2 - d6));
        }

        var va = d3 * d6 - d5 * d4;
        if (va <= 0 && d4 - d3 >= 0 && d5 - d6 >= 0)
        {
            return b + (c - b) * ((d4 - d3) / (d4 - d3 + (d5 - d6)));
        }

        var denom = 1f / (va + vb + vc);
        return a + ab * (vb * denom) + ac * (vc * denom);
    }

    private Node[] BuildHullBVH()
    {
        if (Hulls.Length == 0)
        {
            return [];
        }

        // Build BVH recursively
        var nodes = new List<Node>();
        var sortKeys = new float[Hulls.Length];

        BuildHullBVHRecursive(nodes, HullIndices, sortKeys, 0, Hulls.Length, 0);

        return [.. nodes];
    }

    private void BuildHullBVHRecursive(List<Node> nodes, int[] hullIndices, float[] sortKeys, int start, int count, int depth)
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

        // Sort hulls along split axis by bounds center
        var axisIndex = (int)splitAxis;
        for (var i = start; i < start + count; i++)
        {
            var hull = Hulls[hullIndices[i]];
            sortKeys[i] = (hull.Min[axisIndex] + hull.Max[axisIndex]) * 0.5f;
        }

        Array.Sort(sortKeys, hullIndices, start, count);

        // Split in the middle
        var leftCount = count / 2;
        var rightCount = count - leftCount;

        // Build left child (immediately after parent)
        var leftChildIndex = nodes.Count;
        BuildHullBVHRecursive(nodes, hullIndices, sortKeys, start, leftCount, depth + 1);

        // Build right child
        var rightChildIndex = nodes.Count;
        BuildHullBVHRecursive(nodes, hullIndices, sortKeys, start + leftCount, rightCount, depth + 1);

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

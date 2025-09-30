
using System.Linq;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using Vortice.SpirvCross;
using static ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

namespace GUI.Types.Renderer;

class Rubikon
{
    public record PhysicsMeshData(
        Vector3[] VertexPositions,
        Triangle[] Triangles,
        Node[] PhysicsTree,
        Stack<(Node Node, int Index)> TraversalStack
    );

    public PhysicsMeshData[] Meshes { get; }

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

            Meshes[meshIndex++] = new PhysicsMeshData([.. vertexPositions], [.. triangles], [.. physicsTree], new(capacity: 64));
        }
    }

    public record struct TraceResult(bool Hit, Vector3 HitPosition, int TriangleIndex);

    public TraceResult TraceRay(Vector3 from, Vector3 to)
    {
        foreach (var mesh in Meshes)
        {
            var hit = RayIntersectsWithMesh(mesh, from, to);
            if (hit.Hit)
            {
                return hit;
            }
        }

        return new(false, Vector3.Zero, -1);
    }

    private static TraceResult RayIntersectsWithMesh(PhysicsMeshData mesh, Vector3 from, Vector3 to)
    {
        var dir = Vector3.Normalize(to - from);
        var invDir = Vector3.One / dir;
        var tMin = 0.0f;
        var tMax = Vector3.Distance(from, to);

        var stack = mesh.TraversalStack;
        stack.Clear();
        stack.Push((mesh.PhysicsTree[0], 0));

        var closestT = float.MaxValue;
        var result = new TraceResult(false, Vector3.Zero, -1);

        while (stack.TryPop(out var nodeWithIndex))
        {
            var node = nodeWithIndex.Node;
            var (min, max) = (node.Min, node.Max);

            // Calculate intersection with AABB using slab method
            var t1 = (min - from) * invDir;
            var t2 = (max - from) * invDir;

            var tNear = Vector3.Min(t1, t2);
            var tFar = Vector3.Max(t1, t2);

            var tNearMax = MathF.Max(tNear.X, MathF.Max(tNear.Y, tNear.Z));
            var tFarMin = MathF.Min(tFar.X, MathF.Min(tFar.Y, tFar.Z));

            var nodeIntersects = tNearMax <= tFarMin && tFarMin >= tMin && tNearMax <= tMax;

            if (!nodeIntersects)
            {
                continue;
            }

            if (node.Type != NodeType.Leaf)
            {
                var leftChild = nodeWithIndex.Index + 1;
                var rightChild = nodeWithIndex.Index + (int)node.ChildOffset;

                var rayIsPositive = dir[(int)node.Type] >= 0;
                var (nearId, farId) = rayIsPositive
                    ? (leftChild, rightChild)    // Ray going positive direction, traverse left first
                    : (rightChild, leftChild);   // Ray going negative direction, traverse right first

                // Push far node first so near node is processed first (stack is LIFO)
                stack.Push(new(mesh.PhysicsTree[farId], farId));
                stack.Push(new(mesh.PhysicsTree[nearId], nearId));
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

                // Möller–Trumbore ray-triangle intersection algorithm
                var edge1 = v1 - v0;
                var edge2 = v2 - v0;
                var h = Vector3.Cross(dir, edge2);
                var a = Vector3.Dot(edge1, h);

                // Ray is parallel to triangle
                if (Math.Abs(a) < 1e-6f)
                {
                    continue;
                }

                var f = 1.0f / a;
                var s = from - v0;
                var u = f * Vector3.Dot(s, h);

                // Ray intersection is outside triangle
                if (u is < 0.0f or > 1.0f)
                {
                    continue;
                }

                var q = Vector3.Cross(s, edge1);
                var v = f * Vector3.Dot(dir, q);

                // Ray intersection is outside triangle
                if (v < 0.0f || u + v > 1.0f)
                {
                    continue;
                }

                var t = f * Vector3.Dot(edge2, q);

                // Ray intersection is behind ray origin or beyond ray end
                if (t < tMin || t > tMax)
                {
                    continue;
                }

                // Update if this is the closest hit
                if (t < closestT)
                {
                    closestT = t;
                    result = new(true, from + dir * t, i);
                }
            }
        }

        return result;
    }
}

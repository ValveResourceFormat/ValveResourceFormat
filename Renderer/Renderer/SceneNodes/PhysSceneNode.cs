using System.IO;
using System.Runtime.InteropServices;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using RnHull = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Hull;
using RnMesh = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;
using RnShape = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shape;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes physics collision shapes.
    /// </summary>
    public class PhysSceneNode : ShapeSceneNode
    {
        /// <summary>The opacity of every translucent physics fill, which the order independent composite relies on.</summary>
        internal const float FillAlpha = 0.65f;

        private static readonly Color32 ColorSphere = new(0.4f, 1f, 0.1f, FillAlpha);
        private static readonly Color32 ColorCapsule = new(0.1f, 1f, 0.4f, FillAlpha);
        private static readonly Color32 ColorMesh = new(0.4f, 0.4f, 1f, FillAlpha);
        private static readonly Color32 ColorHull = new(1.0f, 0.2f, 0.1f, FillAlpha);

        // Larger meshes, such as world collision, are split along their collision tree so culling can skip parts of them
        private const int MaxChunkTriangles = 32768;

        // Only mesh edges whose triangles meet at more than this angle are drawn, so bumpy ground is not a web of lines
        private static readonly float CreaseAngleCosine = MathF.Cos(float.DegreesToRadians(40f));

        private readonly record struct GroupMesh(RnMesh Shape, Vector3[] Positions, int FirstTriangle);

        private enum EdgeKind : byte
        {
            Boundary,
            Smooth,
            Crease,
            NonManifold,
        }

        // Edges are keyed by their lower welded vertex, and edges sharing it are chained through Next
        private struct MeshEdge
        {
            public int Higher;
            public int Next;
            public int Triangle;
            public byte Corner;
            public EdgeKind Kind;
        }

        // The triangles of a group's meshes, with the edges between their welded vertices
        private sealed class MeshEdges
        {
            private readonly List<int> firstEdges = [];
            private readonly List<MeshEdge> edges = [];
            private readonly List<Vector3> triangleNormals = [];

            public int TriangleCount => triangleNormals.Count;

            public Vector3 GetNormal(int triangle) => triangleNormals[triangle];

            public void EnsureCapacity(int vertexCount, int triangleCount)
            {
                // A mesh has about as many edges as vertices and triangles together
                edges.EnsureCapacity(vertexCount + triangleCount);
                triangleNormals.EnsureCapacity(triangleCount);
                firstEdges.EnsureCapacity(vertexCount);

                while (firstEdges.Count < vertexCount)
                {
                    firstEdges.Add(-1);
                }
            }

            public void Clear()
            {
                firstEdges.Clear();
                edges.Clear();
                triangleNormals.Clear();
            }

            public void AddTriangle(int a, int b, int c, Vector3 normal)
            {
                var triangle = triangleNormals.Count;
                triangleNormals.Add(normal);

                AddEdge(triangle, 0, a, b);
                AddEdge(triangle, 1, b, c);
                AddEdge(triangle, 2, c, a);
            }

            // A bit per triangle corner whose edge to the next corner is drawn
            public byte[] GetLineCorners()
            {
                var lineCorners = new byte[triangleNormals.Count];

                foreach (var edge in CollectionsMarshal.AsSpan(edges))
                {
                    if (edge.Kind != EdgeKind.Smooth)
                    {
                        lineCorners[edge.Triangle] |= (byte)(1 << edge.Corner);
                    }
                }

                return lineCorners;
            }

            private void AddEdge(int triangle, int corner, int from, int to)
            {
                if (from == to)
                {
                    return;
                }

                var lower = Math.Min(from, to);
                var higher = Math.Max(from, to);
                var allEdges = CollectionsMarshal.AsSpan(edges);
                var first = firstEdges[lower];

                for (var i = first; i >= 0; i = allEdges[i].Next)
                {
                    ref var edge = ref allEdges[i];

                    if (edge.Higher != higher)
                    {
                        continue;
                    }

                    if (edge.Kind == EdgeKind.Boundary)
                    {
                        var isCrease = Vector3.Dot(triangleNormals[edge.Triangle], triangleNormals[triangle]) < CreaseAngleCosine;
                        edge.Kind = isCrease ? EdgeKind.Crease : EdgeKind.Smooth;
                    }
                    else
                    {
                        edge.Kind = EdgeKind.NonManifold;
                    }

                    return;
                }

                firstEdges[lower] = edges.Count;
                edges.Add(new MeshEdge { Higher = higher, Next = first, Triangle = triangle, Corner = (byte)corner });
            }
        }

        /// <inheritdoc/>
        public override bool LayerEnabled => Enabled && base.LayerEnabled;

        /// <inheritdoc/>
        protected override bool IsDoubleSided => false;

        /// <inheritdoc/>
        internal override bool HasOrderIndependentFill => IsTranslucent && IsTranslucentRenderMode;

        /// <summary>Gets or sets whether this physics node is individually enabled for rendering.</summary>
        public bool Enabled
        {
            get;
            set
            {
                field = value;
                Scene.MarkParentOctreeDirty(this);
            }
        }

        /// <summary>
        /// Gets whether these shapes stand for surfaces built from a tools material, such as clips and triggers,
        /// which the compiled world keeps only as collision.
        /// </summary>
        public bool IsToolsMaterial { get; private set; }

        /// <summary>Gets the display name of the collision group represented by this node.</summary>
        public required string PhysGroupName { get; init; }

        /// <summary>Gets the physics this node was built from, or <see langword="null"/> for a node built from raw geometry.</summary>
        public PhysAggregateData? Physics { get; private init; }

        /// <summary>Gets the index into <see cref="PhysAggregateData.CollisionAttributes"/> of the group this node draws.</summary>
        public int CollisionAttributeIndex { get; private init; }

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysSceneNode"/> class from pre-built vertex and index lists.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="verts">The vertex data for the collision geometry.</param>
        /// <param name="inds">The index data for the collision geometry.</param>
        public PhysSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : base(scene, verts, inds)
        {
        }

        private PhysSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds, int lineIndexCount) : base(scene, verts, inds, lineIndexCount)
        {
        }

        /// <summary>
        /// Creates the <see cref="PhysSceneNode"/>s for each collision attribute group from the given physics aggregate data.
        /// A large mesh is split into several nodes along its collision tree, which all share the group's <see cref="PhysGroupName"/>.
        /// </summary>
        /// <param name="scene">The scene to add nodes to.</param>
        /// <param name="phys">The physics aggregate data containing all collision shapes.</param>
        /// <param name="fileName">The source file name, used for the node's <see cref="SceneNode.Name"/>.</param>
        /// <param name="classname">Optional entity classname used to derive tool textures and names.</param>
        public static IEnumerable<PhysSceneNode> CreatePhysSceneNodes(Scene scene, PhysAggregateData phys, string? fileName, string? classname = null)
        {
            var groupCount = phys.CollisionAttributes.Count;
            var physSceneNodes = new List<PhysSceneNode>(groupCount);
            var verts = new List<SimpleVertexNormal>(128);
            var inds = new List<int>(128);
            var lines = new List<int>(128);
            var meshes = new List<GroupMesh>();
            var weldedVertices = new Dictionary<Vector3, int>();
            var meshEdges = new MeshEdges();
            var chunks = new List<Range>();
            var chunkVerts = new List<SimpleVertexNormal>();
            var chunkInds = new List<int>();
            var chunkLines = new List<int>();
            var bindPose = phys.BindPose;

            for (var collisionAttributeIndex = 0; collisionAttributeIndex < phys.CollisionAttributes.Count; collisionAttributeIndex++)
            {
                for (var p = 0; p < phys.Parts.Length; p++)
                {
                    var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                    AddPartShapes(verts, inds, lines, meshes, weldedVertices, meshEdges, phys.Parts[p].Shape, pose, collisionAttributeIndex);
                }

                var attributes = phys.CollisionAttributes[collisionAttributeIndex];
                var tags = PhysAggregateData.GetInteractAsTags(attributes);
                var group = attributes.GetStringProperty("m_CollisionGroupString");

                var toolsMaterial = MapExtract.GetToolsMaterialForInteractAs(tags);

                var physName = string.Empty;

                if (classname != null)
                {
                    physName = classname;
                }
                else
                {
                    if (group != null)
                    {
                        if (group.Equals("default", StringComparison.OrdinalIgnoreCase))
                        {
                            physName = $"- default";
                        }
                        else if (!group.Equals("conditionallysolid", StringComparison.OrdinalIgnoreCase))
                        {
                            physName = group;
                        }
                    }

                    if (tags.Length > 0)
                    {
                        physName = $"[{string.Join(", ", tags)}]" + physName;
                    }

                    if (toolsMaterial != null)
                    {
                        physName = $"- {Path.GetFileNameWithoutExtension(toolsMaterial)} {physName}";
                    }
                }

                string? toolMaterialName = null;

                if (classname != null)
                {
                    var toolTexture = MapExtract.GetToolTextureForEntity(classname);
                    if (!string.IsNullOrEmpty(toolTexture))
                    {
                        toolMaterialName = toolTexture;
                    }
                }
                else
                {
                    toolMaterialName = toolsMaterial;
                }

                var lineCorners = meshEdges.GetLineCorners();
                var firstGroupNode = physSceneNodes.Count;

                foreach (var mesh in meshes)
                {
                    var triangles = mesh.Shape.GetTriangles();

                    AddMeshChunks(mesh.Shape, triangles.Length, chunks);

                    // A mesh that fits in one chunk shares the node of the group's other shapes
                    if (chunks.Count == 1)
                    {
                        AddMeshTriangles(verts, inds, lines, mesh, triangles, meshEdges, lineCorners, chunks[0]);
                    }
                    else
                    {
                        foreach (var chunk in chunks)
                        {
                            AddMeshTriangles(chunkVerts, chunkInds, chunkLines, mesh, triangles, meshEdges, lineCorners, chunk);

                            physSceneNodes.Add(CreateNode(scene, phys, chunkVerts, chunkInds, chunkLines, physName, fileName, collisionAttributeIndex, toolMaterialName));

                            chunkVerts.Clear();
                            chunkInds.Clear();
                            chunkLines.Clear();
                        }
                    }

                    chunks.Clear();
                }

                // Every group gets a node, even an empty one, so that it is listed
                var groupHasNode = physSceneNodes.Count > firstGroupNode;

                if (inds.Count > 0 || !groupHasNode)
                {
                    physSceneNodes.Add(CreateNode(scene, phys, verts, inds, lines, physName, fileName, collisionAttributeIndex, toolMaterialName));
                }

                // PhysSceneNode uploads verts to the gpu and does not keep them around
                verts.Clear();
                inds.Clear();
                lines.Clear();
                meshes.Clear();
                weldedVertices.Clear();
                meshEdges.Clear();
            }

            return physSceneNodes;
        }

        /// <summary>
        /// Creates a node visualizing one part's shapes in the part's own local space, for a
        /// ragdoll that moves the node with the part's rigid body every frame.
        /// </summary>
        public static PhysSceneNode CreatePartPhysSceneNode(Scene scene, PhysAggregateData phys, int partIndex, string? fileName, string physGroupName)
        {
            var verts = new List<SimpleVertexNormal>(128);
            var inds = new List<int>(128);
            var lines = new List<int>(128);
            var meshes = new List<GroupMesh>();
            var meshEdges = new MeshEdges();

            AddPartShapes(verts, inds, lines, meshes, [], meshEdges, phys.Parts[partIndex].Shape, Matrix4x4.Identity, collisionAttributeIndex: -1);

            var lineCorners = meshEdges.GetLineCorners();

            foreach (var mesh in meshes)
            {
                AddMeshTriangles(verts, inds, lines, mesh, mesh.Shape.GetTriangles(), meshEdges, lineCorners, Range.All);
            }

            return CreateNode(scene, physics: null, verts, inds, lines, physGroupName, fileName, collisionAttributeIndex: 0, toolMaterialName: null);
        }

        // Adds one part's shapes of a collision attribute group, or of all groups for -1. Meshes are only
        // collected, as their triangles are added once the edges of all the group's meshes are known.
        private static void AddPartShapes(List<SimpleVertexNormal> verts, List<int> inds, List<int> lines, List<GroupMesh> meshes,
            Dictionary<Vector3, int> weldedVertices, MeshEdges meshEdges, RnShape shape, Matrix4x4 pose, int collisionAttributeIndex)
        {
            foreach (var sphere in shape.GetAllSpheres())
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != sphere.CollisionAttributeIndex)
                {
                    continue;
                }

                //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                var center = Vector3.Transform(sphere.Shape.Center, pose);
                var radius = sphere.Shape.Radius;

                verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                inds.EnsureCapacity(inds.Count + HemisphereTriangles * 6 * 2);

                var firstVertex = verts.Count;

                AddSphere(verts, inds, center, radius, ColorSphere);
                AddSphereLines(lines, firstVertex);
            }

            foreach (var capsule in shape.GetAllCapsules())
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != capsule.CollisionAttributeIndex)
                {
                    continue;
                }

                //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                var center0 = Vector3.Transform(capsule.Shape.Center[0], pose);
                var center1 = Vector3.Transform(capsule.Shape.Center[1], pose);
                var radius = capsule.Shape.Radius;

                verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                inds.EnsureCapacity(inds.Count + CapsuleTriangles * 6);

                var firstVertex = verts.Count;

                AddCapsule(verts, inds, center0, center1, radius, ColorCapsule);
                AddCapsuleLines(lines, firstVertex);
            }

            foreach (var hull in shape.GetAllHulls())
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != hull.CollisionAttributeIndex)
                {
                    continue;
                }

                //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                var vertexPositions = hull.Shape.GetVertexPositions();

                using (var positionsBuffer = new RentedBuffer<Vector3>(vertexPositions.Length))
                using (var drawnVerticesBuffer = new RentedBuffer<int>(vertexPositions.Length))
                {
                    var positions = positionsBuffer.Span;
                    var drawnVertices = drawnVerticesBuffer.Span;

                    for (var i = 0; i < vertexPositions.Length; i++)
                    {
                        positions[i] = Vector3.Transform(vertexPositions[i], pose);
                    }

                    var faces = hull.Shape.GetFaces();
                    var edges = hull.Shape.GetEdges();

                    var numTriangles = edges.Length - faces.Length * 2;
                    verts.EnsureCapacity(verts.Count + numTriangles * 3);
                    inds.EnsureCapacity(inds.Count + numTriangles * 3);

                    foreach (var face in faces)
                    {
                        foreach (var (ai, bi, ci) in RnHull.GetFaceTriangles(edges, face))
                        {
                            var a = positions[ai];
                            var b = positions[bi];
                            var c = positions[ci];

                            var normal = ComputeNormal(a, b, c);

                            var offset = verts.Count;
                            verts.Add(new(a, ColorHull, normal));
                            verts.Add(new(b, ColorHull, normal));
                            verts.Add(new(c, ColorHull, normal));

                            AddTriangle(inds, offset, 0, 1, 2);

                            drawnVertices[ai] = offset;
                            drawnVertices[bi] = offset + 1;
                            drawnVertices[ci] = offset + 2;
                        }
                    }

                    // Each edge once, from its half edge with the lower index
                    for (var i = 0; i < edges.Length; i++)
                    {
                        var twin = edges[i].Twin;

                        if (i < twin)
                        {
                            lines.Add(drawnVertices[edges[i].Origin]);
                            lines.Add(drawnVertices[edges[twin].Origin]);
                        }
                    }
                }
            }

            foreach (var mesh in shape.GetAllMeshes())
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != mesh.CollisionAttributeIndex)
                {
                    continue;
                }

                //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                var triangles = mesh.Shape.GetTriangles();
                var vertices = mesh.Shape.GetVertices();

                var positions = new Vector3[vertices.Length];
                using var weldedBuffer = new RentedBuffer<int>(vertices.Length);
                var welded = weldedBuffer.Span;

                weldedVertices.EnsureCapacity(weldedVertices.Count + vertices.Length);

                // Vertices weld across all meshes of the group, so that edges where meshes meet are not boundaries
                for (var i = 0; i < vertices.Length; i++)
                {
                    positions[i] = Vector3.Transform(vertices[i], pose);

                    ref var weldedVertex = ref CollectionsMarshal.GetValueRefOrAddDefault(weldedVertices, positions[i], out var exists);

                    if (!exists)
                    {
                        weldedVertex = weldedVertices.Count - 1;
                    }

                    welded[i] = weldedVertex;
                }

                meshes.Add(new GroupMesh(mesh.Shape, positions, meshEdges.TriangleCount));
                meshEdges.EnsureCapacity(weldedVertices.Count, meshEdges.TriangleCount + triangles.Length);

                // Triangles are added once all edges of the group are known
                foreach (var tri in triangles)
                {
                    var normal = ComputeNormal(positions[tri.X], positions[tri.Y], positions[tri.Z]);
                    meshEdges.AddTriangle(welded[tri.X], welded[tri.Y], welded[tri.Z], normal);
                }
            }
        }

        // The line indices are appended after the triangle indices
        private static PhysSceneNode CreateNode(Scene scene, PhysAggregateData? physics, List<SimpleVertexNormal> verts, List<int> inds, List<int> lines, string physName, string? fileName, int collisionAttributeIndex, string? toolMaterialName)
        {
            inds.AddRange(lines);

            var boundingBox = new AABB();

            if (verts.Count > 0)
            {
                boundingBox = new AABB(verts[0].Position, verts[0].Position);

                foreach (var vertex in verts)
                {
                    boundingBox = boundingBox.Encapsulate(vertex.Position);
                }
            }

            var physSceneNode = new PhysSceneNode(scene, verts, inds, lines.Count)
            {
                PhysGroupName = physName,
                Name = fileName,
                LocalBoundingBox = boundingBox,
                OverlayRenderOrder = collisionAttributeIndex,
                Physics = physics,
                CollisionAttributeIndex = collisionAttributeIndex,
                Enabled = scene.EnabledPhysicsGroups.Contains(physName),
            };

            if (toolMaterialName != null)
            {
                physSceneNode.SetToolTexture(toolMaterialName);
            }

            return physSceneNode;
        }

        private static void AddMeshTriangles(List<SimpleVertexNormal> verts, List<int> inds, List<int> lines, GroupMesh mesh, ReadOnlySpan<RnMesh.Triangle> triangles, MeshEdges meshEdges, byte[] lineCorners, Range chunk)
        {
            var positions = mesh.Positions;
            var (firstTriangle, triangleCount) = chunk.GetOffsetAndLength(triangles.Length);

            verts.EnsureCapacity(verts.Count + triangleCount * 3);
            inds.EnsureCapacity(inds.Count + triangleCount * 3);

            for (var i = firstTriangle; i < firstTriangle + triangleCount; i++)
            {
                var tri = triangles[i];
                var groupTriangle = mesh.FirstTriangle + i;
                var normal = meshEdges.GetNormal(groupTriangle);

                var offset = verts.Count;
                verts.Add(new(positions[tri.X], ColorMesh, normal));
                verts.Add(new(positions[tri.Y], ColorMesh, normal));
                verts.Add(new(positions[tri.Z], ColorMesh, normal));

                AddTriangle(inds, offset, 0, 1, 2);

                var corners = lineCorners[groupTriangle];

                for (var corner = 0; corner < 3; corner++)
                {
                    if ((corners & (1 << corner)) != 0)
                    {
                        lines.Add(offset + corner);
                        lines.Add(offset + (corner + 1) % 3);
                    }
                }
            }
        }

        // Each subtree of the collision tree holds a contiguous range of triangles. A mesh whose tree does not stays whole.
        private static void AddMeshChunks(RnMesh mesh, int triangleCount, List<Range> chunks)
        {
            if (triangleCount > MaxChunkTriangles)
            {
                var nodes = mesh.ParseNodes();

                if (nodes.Length > 0)
                {
                    AddTreeChunks(nodes, 0, chunks);

                    if (ChunksCoverTriangles(chunks, triangleCount))
                    {
                        return;
                    }

                    chunks.Clear();
                }
            }

            chunks.Add(0..triangleCount);
        }

        // Takes the largest subtrees that fit in a chunk
        private static void AddTreeChunks(ReadOnlySpan<RnMesh.Node> nodes, int index, List<Range> chunks)
        {
            var node = nodes[index];
            var triangles = GetSubtreeTriangles(nodes, index);

            if (node.Type == RnMesh.NodeType.Leaf || triangles.End.Value - triangles.Start.Value <= MaxChunkTriangles)
            {
                chunks.Add(triangles);
                return;
            }

            AddTreeChunks(nodes, index + 1, chunks);
            AddTreeChunks(nodes, index + (int)node.ChildOffset, chunks);
        }

        private static Range GetSubtreeTriangles(ReadOnlySpan<RnMesh.Node> nodes, int index)
        {
            var firstLeaf = index;

            while (nodes[firstLeaf].Type != RnMesh.NodeType.Leaf)
            {
                firstLeaf++;
            }

            var lastLeaf = index;

            while (nodes[lastLeaf].Type != RnMesh.NodeType.Leaf)
            {
                lastLeaf += (int)nodes[lastLeaf].ChildOffset;
            }

            var start = (int)nodes[firstLeaf].TriangleOffset;
            var end = (int)(nodes[lastLeaf].TriangleOffset + nodes[lastLeaf].ChildOffset);

            return start..end;
        }

        private static bool ChunksCoverTriangles(List<Range> chunks, int triangleCount)
        {
            var end = 0;

            foreach (var chunk in chunks)
            {
                if (chunk.Start.Value != end)
                {
                    return false;
                }

                end = chunk.End.Value;
            }

            return end == triangleCount;
        }

        private void SetToolTexture(string toolMaterialName)
        {
            IsToolsMaterial = true;
            ToolTexture = Scene.RendererContext.MaterialLoader.GetMaterial(toolMaterialName, null).Textures.GetValueOrDefault("g_tColor");
        }

        private static Vector3 ComputeNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            var side1 = b - a;
            var side2 = c - a;

            return Vector3.Normalize(Vector3.Cross(side1, side2));
        }
    }
}

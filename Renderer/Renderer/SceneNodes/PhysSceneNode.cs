using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using RnHull = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Hull;
using RnMesh = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Mesh;

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

        private readonly record struct GroupMesh(RnMesh Shape, Vector3[] Positions);

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

        /// <summary>
        /// Initializes a new instance of the <see cref="PhysSceneNode"/> class from pre-built vertex and index lists.
        /// </summary>
        /// <param name="scene">The scene this node belongs to.</param>
        /// <param name="verts">The vertex data for the collision geometry.</param>
        /// <param name="inds">The index data for the collision geometry.</param>
        public PhysSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : base(scene, verts, inds)
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
            var meshes = new List<GroupMesh>();
            var chunks = new List<Range>();
            var chunkVerts = new List<SimpleVertexNormal>();
            var chunkInds = new List<int>();
            var bindPose = phys.BindPose;

            for (var collisionAttributeIndex = 0; collisionAttributeIndex < phys.CollisionAttributes.Count; collisionAttributeIndex++)
            {
                for (var p = 0; p < phys.Parts.Length; p++)
                {
                    var shape = phys.Parts[p].Shape;
                    //var partCollisionAttributeIndex = phys.Parts[p].CollisionAttributeIndex;

                    foreach (var sphere in shape.GetAllSpheres())
                    {
                        if (collisionAttributeIndex != sphere.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                        var center = sphere.Shape.Center;
                        var radius = sphere.Shape.Radius;

                        if (bindPose.Length != 0)
                        {
                            center = Vector3.Transform(center, bindPose[p]);
                        }

                        verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                        inds.EnsureCapacity(inds.Count + HemisphereTriangles * 6 * 2);

                        AddSphere(verts, inds, center, radius, ColorSphere);
                    }

                    foreach (var capsule in shape.GetAllCapsules())
                    {
                        if (collisionAttributeIndex != capsule.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                        var center = capsule.Shape.Center.AsSpan().ToArray();
                        var radius = capsule.Shape.Radius;

                        if (bindPose.Length != 0)
                        {
                            center[0] = Vector3.Transform(center[0], bindPose[p]);
                            center[1] = Vector3.Transform(center[1], bindPose[p]);
                        }

                        verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                        inds.EnsureCapacity(inds.Count + CapsuleTriangles * 6);

                        AddCapsule(verts, inds, center[0], center[1], radius, ColorCapsule);
                    }

                    foreach (var hull in shape.GetAllHulls())
                    {
                        if (collisionAttributeIndex != hull.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                        var vertexPositions = hull.Shape.GetVertexPositions();

                        var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                        using (var positionsBuffer = new RentedBuffer<Vector3>(vertexPositions.Length))
                        {
                            var positions = positionsBuffer.Span;
                            for (var i = 0; i < vertexPositions.Length; i++)
                            {
                                positions[i] = Vector3.Transform(vertexPositions[i], pose);
                            }

                            var faces = hull.Shape.GetFaces();
                            var edges = hull.Shape.GetEdges();

                            var numTriangles = edges.Length - faces.Length * 2;
                            verts.EnsureCapacity(verts.Count + numTriangles * 3);
                            inds.EnsureCapacity(inds.Count + numTriangles * 6);

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
                                }
                            }
                        }
                    }

                    foreach (var mesh in shape.GetAllMeshes())
                    {
                        if (collisionAttributeIndex != mesh.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                        var vertices = mesh.Shape.GetVertices();

                        var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                        var positions = new Vector3[vertices.Length];
                        for (var i = 0; i < vertices.Length; i++)
                        {
                            positions[i] = Vector3.Transform(vertices[i], pose);
                        }

                        meshes.Add(new GroupMesh(mesh.Shape, positions));
                    }
                }

                var attributes = phys.CollisionAttributes[collisionAttributeIndex];
                var tags = PhysAggregateData.GetInteractAsTags(attributes);
                var group = attributes.GetStringProperty("m_CollisionGroupString");

                var tooltexture = MapExtract.GetToolTextureShortenedName_ForInteractStrings([.. tags]);

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

                    if (tooltexture != "nodraw")
                    {
                        physName = $"- {tooltexture} {physName}";
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
                else if (tooltexture != "nodraw")
                {
                    toolMaterialName = $"materials/tools/tools{tooltexture}.vmat";
                }

                var firstGroupNode = physSceneNodes.Count;

                foreach (var mesh in meshes)
                {
                    var triangles = mesh.Shape.GetTriangles();

                    AddMeshChunks(mesh.Shape, triangles.Length, chunks);

                    // A mesh that fits in one chunk shares the node of the group's other shapes
                    if (chunks.Count == 1)
                    {
                        AddMeshTriangles(verts, inds, mesh, triangles, chunks[0]);
                    }
                    else
                    {
                        foreach (var chunk in chunks)
                        {
                            AddMeshTriangles(chunkVerts, chunkInds, mesh, triangles, chunk);

                            physSceneNodes.Add(CreateNode(scene, chunkVerts, chunkInds, physName, fileName, collisionAttributeIndex, toolMaterialName));

                            chunkVerts.Clear();
                            chunkInds.Clear();
                        }
                    }

                    chunks.Clear();
                }

                // Every group gets a node, even an empty one, so that it is listed
                var groupHasNode = physSceneNodes.Count > firstGroupNode;

                if (inds.Count > 0 || !groupHasNode)
                {
                    physSceneNodes.Add(CreateNode(scene, verts, inds, physName, fileName, collisionAttributeIndex, toolMaterialName));
                }

                // PhysSceneNode uploads verts to the gpu and does not keep them around
                verts.Clear();
                inds.Clear();
                meshes.Clear();
            }

            return physSceneNodes;
        }

        private static PhysSceneNode CreateNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds, string physName, string? fileName, int collisionAttributeIndex, string? toolMaterialName)
        {
            var boundingBox = new AABB();

            if (verts.Count > 0)
            {
                boundingBox = new AABB(verts[0].Position, verts[0].Position);

                foreach (var vertex in verts)
                {
                    boundingBox = boundingBox.Encapsulate(vertex.Position);
                }
            }

            var physSceneNode = new PhysSceneNode(scene, verts, inds)
            {
                PhysGroupName = physName,
                Name = fileName,
                LocalBoundingBox = boundingBox,
                OverlayRenderOrder = collisionAttributeIndex,
            };

            if (toolMaterialName != null)
            {
                physSceneNode.SetToolTexture(toolMaterialName);
            }

            return physSceneNode;
        }

        private static void AddMeshTriangles(List<SimpleVertexNormal> verts, List<int> inds, GroupMesh mesh, ReadOnlySpan<RnMesh.Triangle> triangles, Range chunk)
        {
            var positions = mesh.Positions;
            var (firstTriangle, triangleCount) = chunk.GetOffsetAndLength(triangles.Length);

            verts.EnsureCapacity(verts.Count + triangleCount * 3);
            inds.EnsureCapacity(inds.Count + triangleCount * 3);

            for (var i = firstTriangle; i < firstTriangle + triangleCount; i++)
            {
                var tri = triangles[i];
                var a = positions[tri.X];
                var b = positions[tri.Y];
                var c = positions[tri.Z];

                var normal = ComputeNormal(a, b, c);

                var offset = verts.Count;
                verts.Add(new(a, ColorMesh, normal));
                verts.Add(new(b, ColorMesh, normal));
                verts.Add(new(c, ColorMesh, normal));

                AddTriangle(inds, offset, 0, 1, 2);
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

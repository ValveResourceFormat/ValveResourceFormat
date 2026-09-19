using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;
using RnHull = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shapes.Hull;
using RnShape = ValveResourceFormat.ResourceTypes.RubikonPhysics.Shape;

namespace ValveResourceFormat.Renderer.SceneNodes
{
    /// <summary>
    /// Scene node that visualizes physics collision shapes.
    /// </summary>
    public class PhysSceneNode : ShapeSceneNode
    {
        private static readonly Color32 ColorSphere = new(0.4f, 1f, 0.1f, 0.65f);
        private static readonly Color32 ColorCapsule = new(0.1f, 1f, 0.4f, 0.65f);
        private static readonly Color32 ColorMesh = new(0.4f, 0.4f, 1f, 0.65f);
        private static readonly Color32 ColorHull = new(1.0f, 0.2f, 0.1f, 0.65f);

        /// <inheritdoc/>
        public override bool LayerEnabled => Enabled && base.LayerEnabled;

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
        /// Creates one <see cref="PhysSceneNode"/> per collision attribute group from the given physics aggregate data.
        /// </summary>
        /// <param name="scene">The scene to add nodes to.</param>
        /// <param name="phys">The physics aggregate data containing all collision shapes.</param>
        /// <param name="fileName">The source file name, used for the node's <see cref="SceneNode.Name"/>.</param>
        /// <param name="classname">Optional entity classname used to derive tool textures and names.</param>
        public static IEnumerable<PhysSceneNode> CreatePhysSceneNodes(Scene scene, PhysAggregateData phys, string? fileName, string? classname = null)
        {
            var groupCount = phys.CollisionAttributes.Count;
            var physSceneNodes = new PhysSceneNode[groupCount];
            var verts = new List<SimpleVertexNormal>(128);
            var inds = new List<int>(128);
            var boundingBox = new AABB();
            var boundingBoxInitted = false;
            var bindPose = phys.BindPose;

            for (var collisionAttributeIndex = 0; collisionAttributeIndex < phys.CollisionAttributes.Count; collisionAttributeIndex++)
            {
                for (var p = 0; p < phys.Parts.Length; p++)
                {
                    var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                    AddPartShapes(verts, inds, phys.Parts[p].Shape, pose, collisionAttributeIndex,
                        ref boundingBox, ref boundingBoxInitted);
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

                var physSceneNode = new PhysSceneNode(scene, verts, inds)
                {
                    PhysGroupName = physName,
                    Name = fileName,
                    LocalBoundingBox = boundingBox,
                    OverlayRenderOrder = collisionAttributeIndex,
                    Enabled = scene.EnabledPhysicsGroups.Contains(physName),
                };

                if (classname != null)
                {
                    var toolTexture = MapExtract.GetToolTextureForEntity(classname);
                    if (!string.IsNullOrEmpty(toolTexture))
                    {
                        physSceneNode.SetToolTexture(toolTexture);
                    }
                }
                else if (tooltexture != "nodraw")
                {
                    physSceneNode.SetToolTexture($"materials/tools/tools{tooltexture}.vmat");
                }

                physSceneNodes[collisionAttributeIndex] = physSceneNode;

                // PhysSceneNode uploads verts to the gpu and does not keep them around
                verts.Clear();
                inds.Clear();
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
            var boundingBox = new AABB();
            var boundingBoxInitted = false;

            AddPartShapes(verts, inds, phys.Parts[partIndex].Shape, Matrix4x4.Identity, collisionAttributeIndex: -1,
                ref boundingBox, ref boundingBoxInitted);

            return new PhysSceneNode(scene, verts, inds)
            {
                PhysGroupName = physGroupName,
                Name = fileName,
                LocalBoundingBox = boundingBox,
                Enabled = scene.EnabledPhysicsGroups.Contains(physGroupName),
            };
        }

        // The debug geometry of one part's shapes, transformed by a pose, filtered to one
        // collision attribute group; -1 takes them all. The bounds grow around what was added.
        private static void AddPartShapes(List<SimpleVertexNormal> verts, List<int> inds, in RnShape shape,
            in Matrix4x4 pose, int collisionAttributeIndex, ref AABB boundingBox, ref bool boundingBoxInitted)
        {
            foreach (var sphere in shape.Spheres)
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != sphere.CollisionAttributeIndex)
                {
                    continue;
                }

                var center = Vector3.Transform(sphere.Shape.Center, pose);
                var radius = sphere.Shape.Radius;

                verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                inds.EnsureCapacity(inds.Count + HemisphereTriangles * 6 * 2);

                AddSphere(verts, inds, center, radius, ColorSphere);

                GrowBounds(ref boundingBox, ref boundingBoxInitted,
                    new AABB(center + new Vector3(radius), center - new Vector3(radius)));
            }

            foreach (var capsule in shape.Capsules)
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != capsule.CollisionAttributeIndex)
                {
                    continue;
                }

                // Transformed into locals; the descriptor's own array must stay as authored, since
                // entities share one loaded aggregate
                var center0 = Vector3.Transform(capsule.Shape.Center[0], pose);
                var center1 = Vector3.Transform(capsule.Shape.Center[1], pose);
                var radius = capsule.Shape.Radius;

                verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                inds.EnsureCapacity(inds.Count + CapsuleTriangles * 6);

                AddCapsule(verts, inds, center0, center1, radius, ColorCapsule);

                GrowBounds(ref boundingBox, ref boundingBoxInitted,
                    new AABB(center0 + new Vector3(radius), center0 - new Vector3(radius)));
                GrowBounds(ref boundingBox, ref boundingBoxInitted,
                    new AABB(center1 + new Vector3(radius), center1 - new Vector3(radius)));
            }

            foreach (var hull in shape.Hulls)
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != hull.CollisionAttributeIndex)
                {
                    continue;
                }

                var vertexPositions = hull.Shape.GetVertexPositions();

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

                GrowBounds(ref boundingBox, ref boundingBoxInitted, new AABB(hull.Shape.Min, hull.Shape.Max));
            }

            foreach (var mesh in shape.Meshes)
            {
                if (collisionAttributeIndex >= 0 && collisionAttributeIndex != mesh.CollisionAttributeIndex)
                {
                    continue;
                }

                var triangles = mesh.Shape.GetTriangles();
                var vertices = mesh.Shape.GetVertices();

                var numTriangles = triangles.Length;
                verts.EnsureCapacity(verts.Count + numTriangles * 3);
                inds.EnsureCapacity(inds.Count + numTriangles * 6);

                var positions = new Vector3[vertices.Length];
                for (var i = 0; i < vertices.Length; i++)
                {
                    positions[i] = Vector3.Transform(vertices[i], pose);
                }

                foreach (var tri in triangles)
                {
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

                GrowBounds(ref boundingBox, ref boundingBoxInitted, new AABB(mesh.Shape.Min, mesh.Shape.Max));
            }
        }

        private static void GrowBounds(ref AABB boundingBox, ref bool initted, in AABB bbox)
        {
            if (!initted)
            {
                initted = true;
                boundingBox = bbox;
            }
            else
            {
                boundingBox = boundingBox.Union(bbox);
            }
        }

        private void SetToolTexture(string toolMaterialName)
        {
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

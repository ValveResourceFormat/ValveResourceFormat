using System.Buffers;
using System.Linq;
using System.Runtime.InteropServices;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Types.Renderer
{
    class PhysSceneNode : ShapeSceneNode
    {
        private static readonly Color32 ColorSphere = new(0f, 1f, 0f, 0.65f);
        private static readonly Color32 ColorCapsule = new(0f, 1f, 0f, 0.65f);
        private static readonly Color32 ColorMesh = new(0f, 0f, 1f, 0.65f);
        private static readonly Color32 ColorHull = new(1.0f, 0.0f, 0.0f, 0.65f);

        public override bool LayerEnabled => Enabled && base.LayerEnabled;
        public bool Enabled { get; set; }
        public required string PhysGroupName { get; init; }

        public PhysSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : base(scene, verts, inds)
        {
        }

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
                    var shape = phys.Parts[p].Shape;
                    //var partCollisionAttributeIndex = phys.Parts[p].CollisionAttributeIndex;

                    // Spheres
                    foreach (var sphere in shape.Spheres)
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

                        var bbox = new AABB(center + new Vector3(radius),
                                            center - new Vector3(radius));

                        if (!boundingBoxInitted)
                        {
                            boundingBoxInitted = true;
                            boundingBox = bbox;
                        }
                        else
                        {
                            boundingBox = boundingBox.Union(bbox);
                        }
                    }

                    // Capsules
                    foreach (var capsule in shape.Capsules)
                    {
                        if (collisionAttributeIndex != capsule.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                        var center = capsule.Shape.Center;
                        var radius = capsule.Shape.Radius;

                        if (bindPose.Length != 0)
                        {
                            center[0] = Vector3.Transform(center[0], bindPose[p]);
                            center[1] = Vector3.Transform(center[1], bindPose[p]);
                        }

                        verts.EnsureCapacity(verts.Count + HemisphereVerts * 2);
                        inds.EnsureCapacity(inds.Count + CapsuleTriangles * 6);

                        AddCapsule(verts, inds, center[0], center[1], radius, ColorCapsule);

                        foreach (var cn in center)
                        {
                            var bbox = new AABB(cn + new Vector3(radius),
                                                 cn - new Vector3(radius));

                            if (!boundingBoxInitted)
                            {
                                boundingBoxInitted = true;
                                boundingBox = bbox;
                            }
                            else
                            {
                                boundingBox = boundingBox.Union(bbox);
                            }
                        }
                    }

                    // Hulls
                    foreach (var hull in shape.Hulls)
                    {
                        if (collisionAttributeIndex != hull.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                        var vertexPositions = hull.Shape.GetVertexPositions();

                        var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                        // vertex positions
                        var positionsBuffer = ArrayPool<float>.Shared.Rent(vertexPositions.Length * 3);

                        try
                        {
                            var positions = MemoryMarshal.Cast<float, Vector3>(positionsBuffer);
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
                                var startEdge = face.Edge;

                                for (var edge = edges[startEdge].Next; edge != startEdge;)
                                {
                                    var nextEdge = edges[edge].Next;

                                    if (nextEdge == startEdge)
                                    {
                                        break;
                                    }

                                    var a = positions[edges[startEdge].Origin];
                                    var b = positions[edges[edge].Origin];
                                    var c = positions[edges[nextEdge].Origin];

                                    var normal = ComputeNormal(a, b, c);

                                    var offset = verts.Count;
                                    verts.Add(new(a, ColorHull, normal));
                                    verts.Add(new(b, ColorHull, normal));
                                    verts.Add(new(c, ColorHull, normal));

                                    AddTriangle(inds, offset, 0, 1, 2);

                                    edge = nextEdge;
                                }
                            }
                        }
                        finally
                        {
                            ArrayPool<float>.Shared.Return(positionsBuffer);
                        }

                        var bbox = new AABB(hull.Shape.Min, hull.Shape.Max);

                        if (!boundingBoxInitted)
                        {
                            boundingBoxInitted = true;
                            boundingBox = bbox;
                        }
                        else
                        {
                            boundingBox = boundingBox.Union(bbox);
                        }
                    }

                    // Meshes
                    foreach (var mesh in shape.Meshes)
                    {
                        if (collisionAttributeIndex != mesh.CollisionAttributeIndex)
                        {
                            continue;
                        }

                        //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                        var triangles = mesh.Shape.GetTriangles();
                        var vertices = mesh.Shape.GetVertices();

                        var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                        var numTriangles = triangles.Length;
                        verts.EnsureCapacity(verts.Count + numTriangles * 3);
                        inds.EnsureCapacity(inds.Count + numTriangles * 6);

                        // vertex positions
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

                        var bbox = new AABB(mesh.Shape.Min, mesh.Shape.Max);

                        if (!boundingBoxInitted)
                        {
                            boundingBoxInitted = true;
                            boundingBox = bbox;
                        }
                        else
                        {
                            boundingBox = boundingBox.Union(bbox);
                        }
                    }
                }

                var attributes = phys.CollisionAttributes[collisionAttributeIndex];
                var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
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
                };

                if (classname != null)
                {
                    physSceneNode.SetToolTexture(MapExtract.GetToolTextureForEntity(classname));
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

        private void SetToolTexture(string toolMaterialName)
        {
            ToolTexture = Scene.GuiContext.MaterialLoader.GetMaterial(toolMaterialName, null).Textures.GetValueOrDefault("g_tColor");
        }

        private static Vector3 ComputeNormal(Vector3 a, Vector3 b, Vector3 c)
        {
            var side1 = b - a;
            var side2 = c - a;

            return Vector3.Normalize(Vector3.Cross(side1, side2));
        }

        public override void Update(Scene.UpdateContext context)
        {
        }
    }
}

using System.Linq;
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
        public string PhysGroupName { get; set; }

        public PhysSceneNode(Scene scene, List<SimpleVertexNormal> verts, List<int> inds) : base(scene, verts, inds)
        {
        }


        public static IEnumerable<PhysSceneNode> CreatePhysSceneNodes(Scene scene, PhysAggregateData phys, string fileName, string classname = null)
        {
            var groupCount = phys.CollisionAttributes.Count;
            var verts = new List<SimpleVertexNormal>[groupCount];
            var inds = new List<int>[groupCount];
            var boundingBoxes = new AABB[groupCount];
            var boundingBoxInitted = new bool[groupCount];

            // constants for sizes of spheres/capsules
            var hemisphereVerts = SphereBands * SphereSegments + 1;
            var hemisphereTriangles = SphereSegments * (2 * SphereBands - 1);
            var capsuleTriangles = 2 * hemisphereTriangles + 2 * SphereSegments;

            for (var i = 0; i < groupCount; i++)
            {
                verts[i] = [];
                inds[i] = [];
            }

            var bindPose = phys.BindPose;

            for (var p = 0; p < phys.Parts.Length; p++)
            {
                var shape = phys.Parts[p].Shape;
                //var partCollisionAttributeIndex = phys.Parts[p].CollisionAttributeIndex;

                // Spheres
                foreach (var sphere in shape.Spheres)
                {
                    var collisionAttributeIndex = sphere.CollisionAttributeIndex;
                    //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                    var center = sphere.Shape.Center;
                    var radius = sphere.Shape.Radius;

                    if (bindPose.Length != 0)
                    {
                        center = Vector3.Transform(center, bindPose[p]);
                    }

                    verts[collisionAttributeIndex].EnsureCapacity(verts[collisionAttributeIndex].Count + hemisphereVerts);
                    inds[collisionAttributeIndex].EnsureCapacity(inds[collisionAttributeIndex].Count + hemisphereTriangles * 6);

                    AddSphere(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center, radius, ColorSphere);

                    var bbox = new AABB(center + new Vector3(radius),
                                        center - new Vector3(radius));

                    if (!boundingBoxInitted[collisionAttributeIndex])
                    {
                        boundingBoxInitted[collisionAttributeIndex] = true;
                        boundingBoxes[collisionAttributeIndex] = bbox;
                    }
                    else
                    {
                        boundingBoxes[collisionAttributeIndex] = boundingBoxes[collisionAttributeIndex].Union(bbox);
                    }
                }

                // Capsules
                foreach (var capsule in shape.Capsules)
                {
                    var collisionAttributeIndex = capsule.CollisionAttributeIndex;
                    //var surfacePropertyIndex = capsule.SurfacePropertyIndex;
                    var center = capsule.Shape.Center;
                    var radius = capsule.Shape.Radius;

                    if (bindPose.Length != 0)
                    {
                        center[0] = Vector3.Transform(center[0], bindPose[p]);
                        center[1] = Vector3.Transform(center[1], bindPose[p]);
                    }

                    verts[collisionAttributeIndex].EnsureCapacity(verts[collisionAttributeIndex].Count + hemisphereVerts * 2);
                    inds[collisionAttributeIndex].EnsureCapacity(inds[collisionAttributeIndex].Count + capsuleTriangles * 6);

                    AddCapsule(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center[0], center[1], radius, ColorCapsule);
                    foreach (var cn in center)
                    {
                        var bbox = new AABB(cn + new Vector3(radius),
                                             cn - new Vector3(radius));

                        if (!boundingBoxInitted[collisionAttributeIndex])
                        {
                            boundingBoxInitted[collisionAttributeIndex] = true;
                            boundingBoxes[collisionAttributeIndex] = bbox;
                        }
                        else
                        {
                            boundingBoxes[collisionAttributeIndex] = boundingBoxes[collisionAttributeIndex].Union(bbox);
                        }
                    }
                }

                // Hulls
                foreach (var hull in shape.Hulls)
                {
                    var collisionAttributeIndex = hull.CollisionAttributeIndex;
                    //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                    var vertexPositions = hull.Shape.GetVertexPositions();

                    var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                    var shapeVerts = verts[collisionAttributeIndex];
                    var shapeInds = inds[collisionAttributeIndex];

                    // vertex positions
                    var positions = new Vector3[vertexPositions.Length];
                    for (var i = 0; i < vertexPositions.Length; i++)
                    {
                        positions[i] = Vector3.Transform(vertexPositions[i], pose);
                    }


                    var faces = hull.Shape.GetFaces();
                    var edges = hull.Shape.GetEdges();

                    var numTriangles = edges.Length - faces.Length * 2;
                    shapeVerts.EnsureCapacity(shapeVerts.Count + numTriangles * 3);
                    shapeInds.EnsureCapacity(shapeInds.Count + numTriangles * 6);

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

                            var offset = shapeVerts.Count;
                            shapeVerts.Add(new(a, ColorHull, normal));
                            shapeVerts.Add(new(b, ColorHull, normal));
                            shapeVerts.Add(new(c, ColorHull, normal));

                            AddTriangle(shapeInds, offset, 0, 1, 2);

                            edge = nextEdge;
                        }
                    }

                    var bbox = new AABB(hull.Shape.Min, hull.Shape.Max);

                    if (!boundingBoxInitted[collisionAttributeIndex])
                    {
                        boundingBoxInitted[collisionAttributeIndex] = true;
                        boundingBoxes[collisionAttributeIndex] = bbox;
                    }
                    else
                    {
                        boundingBoxes[collisionAttributeIndex] = boundingBoxes[collisionAttributeIndex].Union(bbox);
                    }
                }

                // Meshes
                foreach (var mesh in shape.Meshes)
                {
                    var collisionAttributeIndex = mesh.CollisionAttributeIndex;
                    //var surfacePropertyIndex = capsule.SurfacePropertyIndex;

                    var triangles = mesh.Shape.GetTriangles();
                    var vertices = mesh.Shape.GetVertices();

                    var pose = bindPose.Length == 0 ? Matrix4x4.Identity : bindPose[p];

                    var shapeVerts = verts[collisionAttributeIndex];
                    var shapeInds = inds[collisionAttributeIndex];

                    var numTriangles = triangles.Length;
                    shapeVerts.EnsureCapacity(shapeVerts.Count + numTriangles * 3);
                    shapeInds.EnsureCapacity(shapeInds.Count + numTriangles * 6);

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

                        var offset = shapeVerts.Count;
                        shapeVerts.Add(new(a, ColorMesh, normal));
                        shapeVerts.Add(new(b, ColorMesh, normal));
                        shapeVerts.Add(new(c, ColorMesh, normal));

                        AddTriangle(shapeInds, offset, 0, 1, 2);
                    }

                    var bbox = new AABB(mesh.Shape.Min, mesh.Shape.Max);

                    if (!boundingBoxInitted[collisionAttributeIndex])
                    {
                        boundingBoxInitted[collisionAttributeIndex] = true;
                        boundingBoxes[collisionAttributeIndex] = bbox;
                    }
                    else
                    {
                        boundingBoxes[collisionAttributeIndex] = boundingBoxes[collisionAttributeIndex].Union(bbox);
                    }
                }
            }

            var nodes = phys.CollisionAttributes.Select((attributes, i) =>
            {
                if (verts.Length == 0) // TODO: Remove this
                {
                    return null;
                }

                var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
                var group = attributes.GetStringProperty("m_CollisionGroupString");

                var tooltexture = MapExtract.GetToolTextureShortenedName_ForInteractStrings(new HashSet<string>(tags));

                var name = string.Empty;

                if (group != null)
                {
                    if (group.Equals("default", StringComparison.OrdinalIgnoreCase))
                    {
                        name = $"- default";
                    }
                    else if (!group.Equals("conditionallysolid", StringComparison.OrdinalIgnoreCase))
                    {
                        name = group;
                    }
                }

                if (tags.Length > 0)
                {
                    name = $"[{string.Join(", ", tags)}]" + name;
                }

                var physSceneNode = new PhysSceneNode(scene, verts[i], inds[i])
                {
                    Name = fileName,
                    LocalBoundingBox = boundingBoxes[i],
                };

                if (tooltexture != "nodraw")
                {
                    name = $"- {tooltexture} {name}";
                    physSceneNode.SetToolTexture($"materials/tools/tools{tooltexture}.vmat");
                }

                if (classname != null)
                {
                    physSceneNode.SetToolTexture(MapExtract.GetToolTextureForEntity(classname));
                }

                physSceneNode.PhysGroupName = name;

                return physSceneNode;
            }).ToArray();
            return nodes;
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

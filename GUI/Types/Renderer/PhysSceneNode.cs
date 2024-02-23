using System.Linq;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    class PhysSceneNode : ShapeSceneNode
    {
        public bool Enabled { get; set; }
        public string PhysGroupName { get; set; }

        public PhysSceneNode(Scene scene, List<SimpleVertex> verts, List<int> inds) : base(scene, verts, inds)
        {
        }


        public static IEnumerable<PhysSceneNode> CreatePhysSceneNodes(Scene scene, PhysAggregateData phys, string fileName)
        {
            var groupCount = phys.CollisionAttributes.Count;
            var verts = new List<SimpleVertex>[groupCount];
            var inds = new List<int>[groupCount];
            var boundingBoxes = new AABB[groupCount];
            var boundingBoxInitted = new bool[groupCount];

            for (var i = 0; i < groupCount; i++)
            {
                verts[i] = [];
                inds[i] = [];
            }

            var bindPose = phys.BindPose;
            //m_boneParents

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

                    AddSphere(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center, radius, new(1f, 1f, 0f, 0.3f));

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

                    AddCapsule(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center[0], center[1], radius, new(1f, 1f, 0f, 0.3f));
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
                    var baseVertex = verts[collisionAttributeIndex].Count;

                    var vertexPositions = hull.Shape.GetVertexPositions();

                    verts[collisionAttributeIndex].EnsureCapacity(baseVertex + vertexPositions.Length);

                    foreach (var v in vertexPositions)
                    {
                        var vec = v;
                        if (bindPose.Length != 0)
                        {
                            vec = Vector3.Transform(vec, bindPose[p]);
                        }

                        //color red
                        verts[collisionAttributeIndex].Add(new(vec, new(1f, 0f, 0f, 0.3f)));
                    }

                    var faces = hull.Shape.GetFaces();
                    var edges = hull.Shape.GetEdges();

                    inds[collisionAttributeIndex].EnsureCapacity(inds[collisionAttributeIndex].Count + faces.Length * 6); // TODO: This doesn't account for edges

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

                            AddTriangle(
                                inds[collisionAttributeIndex],
                                baseVertex,
                                edges[startEdge].Origin,
                                edges[edge].Origin,
                                edges[nextEdge].Origin);

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

                    var baseVertex = verts[collisionAttributeIndex].Count;

                    var triangles = mesh.Shape.GetTriangles();
                    var vertices = mesh.Shape.GetVertices();

                    // meshes can be large, so ensure capacity up front
                    verts[collisionAttributeIndex].EnsureCapacity(baseVertex + vertices.Length);
                    inds[collisionAttributeIndex].EnsureCapacity(inds[collisionAttributeIndex].Count + triangles.Length * 6);

                    foreach (var vec in vertices)
                    {
                        var v = vec;
                        if (bindPose.Length != 0)
                        {
                            v = Vector3.Transform(vec, bindPose[p]);
                        }

                        //color blue
                        verts[collisionAttributeIndex].Add(new(v, new(0f, 0f, 1f, 0.3f)));
                    }

                    foreach (var tri in triangles)
                    {
                        AddTriangle(inds[collisionAttributeIndex], baseVertex, tri.X, tri.Y, tri.Z);
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

                if (tooltexture != "nodraw")
                {
                    name = $"- {tooltexture} {name}";
                }

                var physSceneNode = new PhysSceneNode(scene, verts[i], inds[i])
                {
                    Name = fileName,
                    PhysGroupName = name,
                    LocalBoundingBox = boundingBoxes[i],
                };

                return physSceneNode;
            }).ToArray();
            return nodes;
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
            {
                return;
            }
            base.Render(context);
        }

        public override void Update(Scene.UpdateContext context)
        {

        }
    }
}

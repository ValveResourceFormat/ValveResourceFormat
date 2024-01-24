using System.Linq;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;

namespace GUI.Types.Renderer
{
    class PhysSceneNode : SceneNode
    {
        public bool Enabled { get; set; }
        public string PhysGroupName { get; set; }

        readonly Shader shader;
        readonly int indexCount;
        readonly int vboHandle;
        readonly int iboHandle;
        readonly int vaoHandle;

        public PhysSceneNode(Scene scene, List<SimpleVertex> verts, List<int> inds)
            : base(scene)
        {
            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");
            GL.UseProgram(shader.Program);

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * SimpleVertex.SizeInBytes, verts.ToArray(), BufferUsageHint.StaticDraw);

            iboHandle = GL.GenBuffer();
            indexCount = inds.Count;
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, iboHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, inds.Count * sizeof(int), inds.ToArray(), BufferUsageHint.StaticDraw);

            SimpleVertex.BindDefaultShaderLayout(shader.Program);

            GL.BindVertexArray(0);
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

                    AddSphere(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center, radius);

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

                    AddCapsule(verts[collisionAttributeIndex], inds[collisionAttributeIndex], center[0], center[1], radius);
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
                    foreach (var v in hull.Shape.VertexPositions)
                    {
                        var vec = v;
                        if (bindPose.Length != 0)
                        {
                            vec = Vector3.Transform(vec, bindPose[p]);
                        }

                        //color red
                        verts[collisionAttributeIndex].Add(new(vec, new(1f, 0f, 0f, 0.3f)));
                    }

                    foreach (var face in hull.Shape.Faces)
                    {
                        var startEdge = face.Edge;

                        for (var edge = hull.Shape.Edges[startEdge].Next; edge != startEdge;)
                        {
                            var nextEdge = hull.Shape.Edges[edge].Next;

                            if (nextEdge == startEdge)
                            {
                                break;
                            }

                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[startEdge].Origin);
                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[edge].Origin);
                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[edge].Origin);
                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[nextEdge].Origin);
                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[nextEdge].Origin);
                            inds[collisionAttributeIndex].Add(baseVertex + hull.Shape.Edges[startEdge].Origin);

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
                    foreach (var vec in mesh.Shape.Vertices)
                    {
                        var v = vec;
                        if (bindPose.Length != 0)
                        {
                            v = Vector3.Transform(vec, bindPose[p]);
                        }

                        //color blue
                        verts[collisionAttributeIndex].Add(new(v, new(0f, 0f, 1f, 0.3f)));
                    }

                    foreach (var tri in mesh.Shape.Triangles)
                    {
                        inds[collisionAttributeIndex].Add(baseVertex + tri.X);
                        inds[collisionAttributeIndex].Add(baseVertex + tri.Y);
                        inds[collisionAttributeIndex].Add(baseVertex + tri.Y);
                        inds[collisionAttributeIndex].Add(baseVertex + tri.Z);
                        inds[collisionAttributeIndex].Add(baseVertex + tri.Z);
                        inds[collisionAttributeIndex].Add(baseVertex + tri.X);
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

        private static void AddCapsule(List<SimpleVertex> verts, List<int> inds, Vector3 c0, Vector3 c1, float radius)
        {
            var mtx = Matrix4x4.CreateLookAt(c0, c1, Vector3.UnitY);
            mtx.Translation = Vector3.Zero;
            AddSphere(verts, inds, c0, radius);
            AddSphere(verts, inds, c1, radius);

            var baseVertex = verts.Count;

            for (var i = 0; i < 4; i++)
            {
                var vr = new Vector3(
                    MathF.Cos(i * MathF.PI / 2) * radius,
                    MathF.Sin(i * MathF.PI / 2) * radius,
                    0);
                vr = Vector3.Transform(vr, mtx);
                var v = vr + c0;

                //color red
                verts.Add(new(v, new(1f, 0f, 0f, 1f)));
                verts.Add(new(vr + c1, new(1f, 0f, 0f, 1f)));

                inds.Add(baseVertex + i * 2);
                inds.Add(baseVertex + i * 2 + 1);
            }
        }

        private static void AddSphere(List<SimpleVertex> verts, List<int> inds, Vector3 center, float radius)
        {
            AddCircle(verts, inds, center, radius, Matrix4x4.Identity);
            AddCircle(verts, inds, center, radius, Matrix4x4.CreateRotationX(MathF.PI * 0.5f));
            AddCircle(verts, inds, center, radius, Matrix4x4.CreateRotationY(MathF.PI * 0.5f));
        }

        private static void AddCircle(List<SimpleVertex> verts, List<int> inds, Vector3 center, float radius, Matrix4x4 mtx)
        {
            var baseVertex = verts.Count;
            for (var i = 0; i < 16; i++)
            {
                var v = new Vector3(
                    MathF.Cos(i * MathF.PI / 8) * radius,
                    MathF.Sin(i * MathF.PI / 8) * radius,
                    0);
                v = Vector3.Transform(v, mtx) + center;

                // color red
                verts.Add(new(v, new(1f, 0f, 0f, 1f)));

                inds.Add(baseVertex + i);
                inds.Add(baseVertex + (i + 1) % 16);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
            {
                return;
            }

            var renderShader = context.ReplacementShader ?? shader;

            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.UseProgram(renderShader.Program);

            renderShader.SetUniform4x4("transform", Transform);
            renderShader.SetUniform1("bAnimated", 0.0f);
            renderShader.SetUniform1("sceneObjectId", Id);

            GL.BindVertexArray(vaoHandle);

            GL.Enable(EnableCap.PolygonOffsetLine);
            GL.Enable(EnableCap.PolygonOffsetFill);
            GL.PolygonOffset(-1, -1);

            //GL.LineWidth(1.5f);
            GL.DrawElements(PrimitiveType.Lines, indexCount, DrawElementsType.UnsignedInt, 0);

            // triangles
            GL.DrawElements(PrimitiveType.Triangles, indexCount, DrawElementsType.UnsignedInt, 0);

            GL.Disable(EnableCap.Blend);
            GL.Disable(EnableCap.PolygonOffsetLine);
            GL.Disable(EnableCap.PolygonOffsetFill);
            GL.DepthMask(true);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {

        }
    }
}

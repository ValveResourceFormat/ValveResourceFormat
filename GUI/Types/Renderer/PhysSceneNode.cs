using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
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
        readonly bool hasUntriangulatedVertices;

        public PhysSceneNode(Scene scene, List<float> verts, List<int> inds, bool hasUntriangulatedVertices)
            : base(scene)
        {
            this.hasUntriangulatedVertices = hasUntriangulatedVertices;

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.default");
            GL.UseProgram(shader.Program);

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);

            vboHandle = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, verts.Count * sizeof(float), verts.ToArray(), BufferUsageHint.StaticDraw);

            iboHandle = GL.GenBuffer();
            indexCount = inds.Count;
            GL.BindBuffer(BufferTarget.ElementArrayBuffer, iboHandle);
            GL.BufferData(BufferTarget.ElementArrayBuffer, inds.Count * sizeof(int), inds.ToArray(), BufferUsageHint.StaticDraw);

            const int stride = sizeof(float) * 7;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
        }

        public static IEnumerable<PhysSceneNode> CreatePhysSceneNodes(Scene scene, PhysAggregateData phys, string fileName)
        {
            var groupCount = phys.CollisionAttributes.Count;
            var verts = new List<float>[groupCount];
            var inds = new List<int>[groupCount];
            var boundingBoxes = new AABB[groupCount];
            var boundingBoxInitted = new bool[groupCount];
            var hasUntriangulatedVertices = new bool[groupCount];

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

                    hasUntriangulatedVertices[collisionAttributeIndex] = true; // TODO: Remove this

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

                    hasUntriangulatedVertices[collisionAttributeIndex] = true; // TODO: Remove this

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

                    var vertOffset = verts[collisionAttributeIndex].Count / 7;
                    foreach (var v in hull.Shape.VertexPositions)
                    {
                        var vec = v;
                        if (bindPose.Length != 0)
                        {
                            vec = Vector3.Transform(vec, bindPose[p]);
                        }

                        verts[collisionAttributeIndex].Add(vec.X);
                        verts[collisionAttributeIndex].Add(vec.Y);
                        verts[collisionAttributeIndex].Add(vec.Z);
                        //color red
                        verts[collisionAttributeIndex].Add(1);
                        verts[collisionAttributeIndex].Add(0);
                        verts[collisionAttributeIndex].Add(0);
                        verts[collisionAttributeIndex].Add(0.3f);
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

                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[startEdge].Origin);
                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[edge].Origin);
                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[edge].Origin);
                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[nextEdge].Origin);
                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[nextEdge].Origin);
                            inds[collisionAttributeIndex].Add(vertOffset + hull.Shape.Edges[startEdge].Origin);

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

                    var vertOffset = verts[collisionAttributeIndex].Count / 7;
                    foreach (var vec in mesh.Shape.Vertices)
                    {
                        var v = vec;
                        if (bindPose.Length != 0)
                        {
                            v = Vector3.Transform(vec, bindPose[p]);
                        }

                        verts[collisionAttributeIndex].Add(v.X);
                        verts[collisionAttributeIndex].Add(v.Y);
                        verts[collisionAttributeIndex].Add(v.Z);
                        //color blue
                        verts[collisionAttributeIndex].Add(0);
                        verts[collisionAttributeIndex].Add(0);
                        verts[collisionAttributeIndex].Add(1);
                        verts[collisionAttributeIndex].Add(0.3f);
                    }

                    foreach (var tri in mesh.Shape.Triangles)
                    {
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[0]);
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[1]);
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[1]);
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[2]);
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[2]);
                        inds[collisionAttributeIndex].Add(vertOffset + tri.Indices[0]);
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

            var scenes = phys.CollisionAttributes.Select((attributes, i) =>
            {
                if (verts.Length == 0) // TODO: Remove this
                {
                    return null;
                }

                var tags = attributes.GetArray<string>("m_InteractAsStrings") ?? attributes.GetArray<string>("m_PhysicsTagStrings");
                var group = attributes.GetStringProperty("m_CollisionGroupString");

                var name = $"[{string.Join(", ", tags)}]";
                if (group != null)
                {
                    name = $"{name} {group}";
                }

                var physSceneNode = new PhysSceneNode(scene, verts[i], inds[i], hasUntriangulatedVertices[i])
                {
                    Name = fileName,
                    PhysGroupName = name,
                    LocalBoundingBox = boundingBoxes[i],
                };

                return physSceneNode;
            }).ToArray();
            return scenes;
        }

        private static void AddCapsule(List<float> verts, List<int> inds, Vector3 c0, Vector3 c1, float radius)
        {
            var mtx = Matrix4x4.CreateLookAt(c0, c1, Vector3.UnitY);
            mtx.Translation = Vector3.Zero;
            AddSphere(verts, inds, c0, radius);
            AddSphere(verts, inds, c1, radius);

            var vertOffset = verts.Count / 7;

            for (var i = 0; i < 4; i++)
            {
                var vr = new Vector3(
                    MathF.Cos(i * MathF.PI / 2) * radius,
                    MathF.Sin(i * MathF.PI / 2) * radius,
                    0);
                vr = Vector3.Transform(vr, mtx);
                var v = vr + c0;

                verts.Add(v.X);
                verts.Add(v.Y);
                verts.Add(v.Z);
                //color red
                verts.Add(1);
                verts.Add(0);
                verts.Add(0);
                verts.Add(1);

                v = vr + c1;

                verts.Add(v.X);
                verts.Add(v.Y);
                verts.Add(v.Z);
                //color red
                verts.Add(1);
                verts.Add(0);
                verts.Add(0);
                verts.Add(1);

                inds.Add(vertOffset + i * 2);
                inds.Add(vertOffset + i * 2 + 1);
            }
        }

        private static void AddSphere(List<float> verts, List<int> inds, Vector3 center, float radius)
        {
            AddCircle(verts, inds, center, radius, Matrix4x4.Identity);
            AddCircle(verts, inds, center, radius, Matrix4x4.CreateRotationX(MathF.PI * 0.5f));
            AddCircle(verts, inds, center, radius, Matrix4x4.CreateRotationY(MathF.PI * 0.5f));
        }

        private static void AddCircle(List<float> verts, List<int> inds, Vector3 center, float radius, Matrix4x4 mtx)
        {
            var vertOffset = verts.Count / 7;
            for (var i = 0; i < 16; i++)
            {
                var v = new Vector3(
                    MathF.Cos(i * MathF.PI / 8) * radius,
                    MathF.Sin(i * MathF.PI / 8) * radius,
                    0);
                v = Vector3.Transform(v, mtx) + center;

                verts.Add(v.X);
                verts.Add(v.Y);
                verts.Add(v.Z);
                //color red
                verts.Add(1);
                verts.Add(0);
                verts.Add(1);
                verts.Add(1);

                inds.Add(vertOffset + i);
                inds.Add(vertOffset + (i + 1) % 16);
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

            if (!hasUntriangulatedVertices)
            {
                // triangles
                GL.DrawElements(PrimitiveType.TrianglesAdjacency, indexCount, DrawElementsType.UnsignedInt, 0);
            }

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

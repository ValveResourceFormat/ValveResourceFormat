using System;
using System.Collections.Generic;
using OpenTK.Graphics.OpenGL;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using System.Numerics;
using System.Linq;

namespace GUI.Types.Renderer
{
    internal class PhysSceneNode : SceneNode
    {
        public bool Enabled { get; set; }
        PhysAggregateData phys;
        Shader shader;
        int indexCount;
        int vboHandle;
        int iboHandle;
        int vaoHandle;

        public PhysSceneNode(Scene scene, PhysAggregateData phys)
            : base(scene)
        {
            this.phys = phys;

            var verts = new List<float>();
            var inds = new List<int>();

            var parts = phys.Data.GetArray("m_parts");
            foreach (var p in parts)
            {
                var shape = p.GetSubCollection("m_rnShape");

                var spheres = shape.GetArray("m_spheres");
                foreach (var s in spheres)
                {
                    var sphere = s.GetSubCollection("m_Sphere");
                    var center = sphere.GetSubCollection("m_vCenter").ToVector3();
                    var radius = sphere.GetFloatProperty("m_flRadius");

                    AddSphere(verts, inds, center, radius);
                }

                var capsules = shape.GetArray("m_capsules");
                foreach (var c in capsules)
                {
                    var capsule = c.GetSubCollection("m_Capsule");
                    var center = capsule.GetArray("m_vCenter");
                    var radius = capsule.GetFloatProperty("m_flRadius");

                    AddCapsule(verts, inds, center[0].ToVector3(), center[1].ToVector3(), radius);
                }
                var hulls = shape.GetArray("m_hulls");
                foreach (var h in hulls)
                {
                    var hull = h.GetSubCollection("m_Hull");
                    //m_vCentroid
                    //m_flMaxAngularRadius
                    //m_Vertices
                    var vertices = hull.GetArray("m_Vertices");
                    var vertOffset = verts.Count / 7;
                    foreach (var v in vertices)
                    {
                        var vec = v.ToVector3();
                        verts.Add(vec.X);
                        verts.Add(vec.Y);
                        verts.Add(vec.Z);
                        //color red
                        verts.Add(1);
                        verts.Add(0);
                        verts.Add(0);
                        verts.Add(1);
                    }
                    //m_Planes
                    var edges = hull.GetArray("m_Edges");
                    foreach (var e in edges)
                    {
                        inds.Add((int)(vertOffset + e.GetIntegerProperty("m_nOrigin")));
                        var next = edges[e.GetIntegerProperty("m_nNext")];
                        inds.Add((int)(vertOffset + next.GetIntegerProperty("m_nOrigin")));
                    }
                    //m_Faces
                    //m_Bounds
                }
                var meshes = shape.GetArray("m_meshes");
                foreach (var m in meshes)
                {
                    var mesh = m.GetSubCollection("m_Mesh");
                    //m_Nodes

                    var vertOffset = verts.Count / 7;
                    Vector3[] vertices = null;
                    try
                    {
                        //NTRO has vertcices as array of structs
                        var verticesArr = mesh.GetArray("m_Vertices");
                        vertices = verticesArr.Select(v => v.ToVector3()).ToArray();
                    }
                    catch
                    {
                        //KV3 has vertices as blob
                        var verticesBlob = mesh.GetArray<byte>("m_Vertices");
                        vertices = Enumerable.Range(0, verticesBlob.Length / 12)
                            .Select(i => new Vector3(BitConverter.ToSingle(verticesBlob, i * 12),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 4),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 8)))
                            .ToArray();
                    }

                    foreach (var v in vertices)
                    {
                        verts.Add(v.X);
                        verts.Add(v.Y);
                        verts.Add(v.Z);
                        //color red
                        verts.Add(0);
                        verts.Add(1);
                        verts.Add(0);
                        verts.Add(1);
                    }

                    int[] triangles = null;
                    try
                    {
                        //NTRO has triangles as array of structs
                        var trianglesArr = mesh.GetArray("m_Triangles");
                        triangles = trianglesArr.SelectMany(t => t.GetArray<int>("m_nIndex")).ToArray();
                    }
                    catch
                    {
                        //KV3 has triangles as blob
                        var trianglesBlob = mesh.GetArray<byte>("m_Triangles");
                        triangles = new int[trianglesBlob.Length / 4];
                        System.Buffer.BlockCopy(trianglesBlob, 0, triangles, 0, trianglesBlob.Length);
                    }

                    for (int i = 0; i < triangles.Length; i += 3)
                    {
                        inds.Add(vertOffset + triangles[i]);
                        inds.Add(vertOffset + triangles[i + 1]);
                        inds.Add(vertOffset + triangles[i + 1]);
                        inds.Add(vertOffset + triangles[i + 2]);
                        inds.Add(vertOffset + triangles[i + 2]);
                        inds.Add(vertOffset + triangles[i]);
                    }
                }
                //m_CollisionAttributeIndices

                //Console.WriteLine($"Phys mesh verts {verts.Count} inds {inds.Count}");
            }

            LocalBoundingBox = new AABB(-10, -10, -10, 10, 10, 10);

            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.grid", new Dictionary<string, bool>());
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

        private static void AddCapsule(List<float> verts, List<int> inds, Vector3 c0, Vector3 c1, float radius)
        {
            Matrix4x4 mtx = Matrix4x4.CreateLookAt(c0, c1, Vector3.UnitY);
            mtx.Translation = Vector3.Zero;
            AddSphere(verts, inds, c0, radius);
            AddSphere(verts, inds, c1, radius);

            var vertOffset = verts.Count / 7;

            for (int i = 0; i < 4; i++)
            {
                Vector3 vr = new Vector3(
                    MathF.Cos(i * MathF.PI / 2) * radius,
                    MathF.Sin(i * MathF.PI / 2) * radius,
                    0);
                vr = Vector3.Transform(vr, mtx);
                Vector3 v = vr + c0;

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
            for (int i = 0; i < 16; i++)
            {
                Vector3 v = new Vector3(
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
                verts.Add(0);
                verts.Add(1);

                inds.Add(vertOffset + i);
                inds.Add(vertOffset + (i + 1) % 16);
            }
        }

        public override void Render(Scene.RenderContext context)
        {
            if (!Enabled)
                return;

            var viewProjectionMatrix = context.Camera.ViewProjectionMatrix.ToOpenTK();

            GL.UseProgram(shader.Program);

            GL.UniformMatrix4(shader.GetUniformLocation("uProjectionViewMatrix"), false, ref viewProjectionMatrix);
            GL.DepthMask(false);

            GL.BindVertexArray(vaoHandle);
            GL.DrawElements(PrimitiveType.Lines, indexCount, DrawElementsType.UnsignedInt, 0);
            GL.BindVertexArray(0);

            GL.DepthMask(true);
        }

        public override void Update(Scene.UpdateContext context)
        {

        }
    }
}

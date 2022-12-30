using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization;
using ValveResourceFormat.Serialization.KeyValues;
using ValveResourceFormat.Serialization.NTRO;

namespace GUI.Types.Renderer
{
    internal class PhysSceneNode : SceneNode
    {
        public bool Enabled { get; set; }
        public bool IsTrigger { get; set; }

        readonly Shader shader;
        readonly int indexCount;
        readonly int vboHandle;
        readonly int iboHandle;
        readonly int vaoHandle;

        public PhysSceneNode(Scene scene, PhysAggregateData phys)
            : base(scene)
        {
            var verts = new List<float>();
            var inds = new List<int>();

            var bindPose = phys.Data.GetArray("m_bindPose")
                 .Select(v => Matrix4x4FromArray(v
                    .Select(m => Convert.ToSingle(m.Value, CultureInfo.InvariantCulture))
                    .ToArray()))
                 .ToArray();

            if (bindPose.Length == 0)
            {
                bindPose = new Matrix4x4[] { Matrix4x4.Identity };
            }
            //m_boneParents

            var firstBbox = true;

            var parts = phys.Data.GetArray("m_parts");
            for (var p = 0; p < parts.Length; p++)
            {
                var shape = parts[p].GetSubCollection("m_rnShape");

                var spheres = shape.GetArray("m_spheres");
                foreach (var s in spheres)
                {
                    var sphere = s.GetSubCollection("m_Sphere");
                    var center = sphere.GetSubCollection("m_vCenter").ToVector3();
                    var radius = sphere.GetFloatProperty("m_flRadius");

                    if (bindPose.Any())
                    {
                        center = Vector3.Transform(center, bindPose[p]);
                    }

                    AddSphere(verts, inds, center, radius);

                    var bbox = new AABB(center + new Vector3(radius),
                                         center - new Vector3(radius));
                    LocalBoundingBox = firstBbox ? bbox : LocalBoundingBox.Union(bbox);
                    firstBbox = false;
                }

                var capsules = shape.GetArray("m_capsules");
                foreach (var c in capsules)
                {
                    var capsule = c.GetSubCollection("m_Capsule");
                    var center = capsule.GetArray("m_vCenter").Select(v => v.ToVector3()).ToArray();
                    var radius = capsule.GetFloatProperty("m_flRadius");

                    center[0] = Vector3.Transform(center[0], bindPose[p]);
                    center[1] = Vector3.Transform(center[1], bindPose[p]);

                    AddCapsule(verts, inds, center[0], center[1], radius);
                    foreach (var cn in center)
                    {
                        var bbox = new AABB(cn + new Vector3(radius),
                                             cn - new Vector3(radius));
                        LocalBoundingBox = firstBbox ? bbox : LocalBoundingBox.Union(bbox);
                        firstBbox = false;
                    }
                }
                var hulls = shape.GetArray("m_hulls");
                foreach (var h in hulls)
                {
                    var hull = h.GetSubCollection("m_Hull");

                    // NTRO and some older KV3 versions had arrays, new KV3 has binary blobs
                    var isOldNonBinaryBlobs = hull is NTROStruct || ((KVObject)hull).Properties["m_Edges"].Type == KVType.ARRAY;

                    //m_vCentroid
                    //m_flMaxAngularRadius
                    //m_Vertices
                    IEnumerable<Vector3> vertices = null;
                    if (isOldNonBinaryBlobs)
                    {
                        var verticesArr = hull.GetArray("m_Vertices");
                        vertices = verticesArr.Select(v => v.ToVector3());
                    }
                    else
                    {
                        var verticesBlob = hull.GetArray<byte>("m_Vertices");
                        vertices = Enumerable.Range(0, verticesBlob.Length / 12)
                            .Select(i => new Vector3(BitConverter.ToSingle(verticesBlob, i * 12),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 4),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 8)));
                    }
                    var vertOffset = verts.Count / 7;
                    foreach (var v in vertices)
                    {
                        var vec = v;
                        if (bindPose.Any())
                        {
                            vec = Vector3.Transform(vec, bindPose[p]);
                        }

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
                    (int origin, int next)[] edges = null;
                    if (isOldNonBinaryBlobs)
                    {
                        var edgesArr = hull.GetArray("m_Edges");
                        edges = edgesArr
                            .Select(e => (e.GetInt32Property("m_nOrigin"), e.GetInt32Property("m_nNext")))
                            .ToArray();
                    }
                    else
                    {
                        // 0 - m_nNext, 1 - m_nTwin, 2 - m_nOrigin, 3 - m_nFace
                        var edgesBlob = hull.GetArray<byte>("m_Edges");
                        edges = Enumerable.Range(0, edgesBlob.Length / 4)
                            .Select(i => ((int)edgesBlob[i * 4 + 2], (int)edgesBlob[i * 4 + 1]))
                            .ToArray();
                    }
                    foreach (var e in edges)
                    {
                        inds.Add(vertOffset + e.origin);
                        var next = edges[e.next];
                        inds.Add(vertOffset + next.origin);
                    }
                    //m_Faces
                    var bounds = hull.GetSubCollection("m_Bounds");
                    var bbox = new AABB(bounds.GetSubCollection("m_vMinBounds").ToVector3(),
                                         bounds.GetSubCollection("m_vMaxBounds").ToVector3());

                    LocalBoundingBox = firstBbox ? bbox : LocalBoundingBox.Union(bbox);
                    firstBbox = false;
                }
                var meshes = shape.GetArray("m_meshes");
                foreach (var m in meshes)
                {
                    var mesh = m.GetSubCollection("m_Mesh");
                    //m_Nodes

                    var vertOffset = verts.Count / 7;
                    Vector3[] vertices = null;
                    if (mesh is NTROStruct)
                    {
                        //NTRO has vertices as array of structs
                        var verticesArr = mesh.GetArray("m_Vertices");
                        vertices = verticesArr.Select(v => v.ToVector3()).ToArray();
                    }
                    else
                    {
                        //KV3 has vertices as blob
                        var verticesBlob = mesh.GetArray<byte>("m_Vertices");
                        vertices = Enumerable.Range(0, verticesBlob.Length / 12)
                            .Select(i => new Vector3(BitConverter.ToSingle(verticesBlob, i * 12),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 4),
                                BitConverter.ToSingle(verticesBlob, (i * 12) + 8)))
                            .ToArray();
                    }

                    foreach (var vec in vertices)
                    {
                        var v = vec;
                        if (bindPose.Any())
                        {
                            v = Vector3.Transform(vec, bindPose[p]);
                        }

                        verts.Add(v.X);
                        verts.Add(v.Y);
                        verts.Add(v.Z);
                        //color green
                        verts.Add(0);
                        verts.Add(1);
                        verts.Add(0);
                        verts.Add(1);
                    }

                    int[] triangles = null;
                    if (mesh is NTROStruct)
                    {
                        //NTRO and SOME KV3 has triangles as array of structs
                        var trianglesArr = mesh.GetArray("m_Triangles");
                        triangles = trianglesArr.SelectMany(t => t.GetArray<object>("m_nIndex").
                                                            Select(Convert.ToInt32)).ToArray();
                    }
                    else
                    {
                        //some KV3 has triangles as blob
                        var trianglesBlob = mesh.GetArray<byte>("m_Triangles");
                        triangles = new int[trianglesBlob.Length / 4];
                        System.Buffer.BlockCopy(trianglesBlob, 0, triangles, 0, trianglesBlob.Length);
                    }

                    for (var i = 0; i < triangles.Length; i += 3)
                    {
                        inds.Add(vertOffset + triangles[i]);
                        inds.Add(vertOffset + triangles[i + 1]);
                        inds.Add(vertOffset + triangles[i + 1]);
                        inds.Add(vertOffset + triangles[i + 2]);
                        inds.Add(vertOffset + triangles[i + 2]);
                        inds.Add(vertOffset + triangles[i]);
                    }

                    var bbox = new AABB(mesh.GetSubCollection("m_vMin").ToVector3(),
                                         mesh.GetSubCollection("m_vMax").ToVector3());
                    LocalBoundingBox = firstBbox ? bbox : LocalBoundingBox.Union(bbox);
                    firstBbox = false;
                }
                //m_CollisionAttributeIndices

                //Console.WriteLine($"Phys mesh verts {verts.Count} inds {inds.Count}");
            }

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

        static Matrix4x4 Matrix4x4FromArray(float[] a)
        {
            return new Matrix4x4(a[0], a[4], a[8], 0,
                a[1], a[5], a[9], 0,
                a[2], a[6], a[10], 0,
                a[3], a[7], a[11], 1);
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
                verts.Add(0);
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

            var viewProjectionMatrix = (Transform * context.Camera.ViewProjectionMatrix).ToOpenTK();

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

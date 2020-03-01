using System;
using System.Collections.Generic;
using System.Numerics;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class OctreeDebugRenderer<T>
        where T : class
    {
        private readonly Shader shader;
        private readonly Octree<T> octree;
        private readonly int vaoHandle;
        private readonly int vboHandle;
        private int vertexCount;
        private bool dynamic;
        public AABB BoundingBox { get; }

        public OctreeDebugRenderer(Octree<T> octree, VrfGuiContext guiContext, bool dynamic)
        {
            this.octree = octree;
            this.dynamic = dynamic;
            BoundingBox = octree.Root.Region;

            shader = shader = guiContext.ShaderLoader.LoadShader("vrf.grid", new Dictionary<string, bool>());
            GL.UseProgram(shader.Program);

            vboHandle = GL.GenBuffer();
            if (!dynamic)
            {
                Rebuild();
            }

            vaoHandle = GL.GenVertexArray();
            GL.BindVertexArray(vaoHandle);
            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);

            const int stride = sizeof(float) * 7;
            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var colorAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
        }

        private void AddLine(List<float> vertices, Vector3 from, Vector3 to, float r, float g, float b, float a)
        {
            vertices.Add(from.X);
            vertices.Add(from.Y);
            vertices.Add(from.Z);
            vertices.Add(r);
            vertices.Add(g);
            vertices.Add(b);
            vertices.Add(a);
            vertices.Add(to.X);
            vertices.Add(to.Y);
            vertices.Add(to.Z);
            vertices.Add(r);
            vertices.Add(g);
            vertices.Add(b);
            vertices.Add(a);
        }

        private void AddBox(List<float> vertices, AABB box, float r, float g, float b, float a)
        {
            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Min.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Min.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Min.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Min.Z), r, g, b, a);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Max.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Max.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), r, g, b, a);

            AddLine(vertices, new Vector3(box.Min.X, box.Min.Y, box.Min.Z), new Vector3(box.Min.X, box.Min.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Min.Y, box.Min.Z), new Vector3(box.Max.X, box.Min.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Max.X, box.Max.Y, box.Min.Z), new Vector3(box.Max.X, box.Max.Y, box.Max.Z), r, g, b, a);
            AddLine(vertices, new Vector3(box.Min.X, box.Max.Y, box.Min.Z), new Vector3(box.Min.X, box.Max.Y, box.Max.Z), r, g, b, a);
        }

        private void AddOctreeNode(List<float> vertices, Octree<T>.Node node, int depth)
        {
            AddBox(vertices, node.Region, 1.0f, 1.0f, 1.0f, node.HasElements ? 1.0f : 0.1f);

            if (node.HasElements)
            {
                foreach (var element in node.Elements)
                {
                    var shading = System.Math.Min(1.0f, depth * 0.1f);
                    AddBox(vertices, element.BoundingBox, 1.0f, shading, 0.0f, 1.0f);

                    // AddLine(vertices, element.BoundingBox.Min, node.Region.Min, 1.0f, shading, 0.0f, 0.5f);
                    // AddLine(vertices, element.BoundingBox.Max, node.Region.Max, 1.0f, shading, 0.0f, 0.5f);
                }
            }

            if (node.HasChildren)
            {
                foreach (var child in node.Children)
                {
                    AddOctreeNode(vertices, child, depth + 1);
                }
            }
        }

        private void Rebuild()
        {
            var vertices = new List<float>();
            AddOctreeNode(vertices, octree.Root, 0);
            vertexCount = vertices.Count / 7;

            GL.BindBuffer(BufferTarget.ArrayBuffer, vboHandle);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), dynamic ? BufferUsageHint.DynamicDraw : BufferUsageHint.StaticDraw);
        }

        public void Update(float frameTime)
        {
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            if (renderPass == RenderPass.Translucent || renderPass == RenderPass.Both)
            {
                if (dynamic)
                {
                    Rebuild();
                }

                GL.Enable(EnableCap.Blend);
                GL.Enable(EnableCap.DepthTest);
                GL.DepthMask(false);
                GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
                GL.UseProgram(shader.Program);

                var projectionViewMatrix = camera.ViewProjectionMatrix.ToOpenTK();
                GL.UniformMatrix4(shader.GetUniformLocation("uProjectionViewMatrix"), false, ref projectionViewMatrix);

                GL.BindVertexArray(vaoHandle);
                GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
                GL.BindVertexArray(0);
                GL.UseProgram(0);
                GL.DepthMask(true);
                GL.Disable(EnableCap.Blend);
                GL.Disable(EnableCap.DepthTest);
            }
        }
    }
}

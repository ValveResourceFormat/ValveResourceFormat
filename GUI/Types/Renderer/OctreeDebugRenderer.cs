using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;
using GUI.Types.Renderer;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class OctreeDebugRenderer<T> : IRenderer
        where T : IOctreeElement
    {
        private const string VertextShaderSource = @"
            #version 330
            in vec3 aVertexPosition;
            in vec4 aVertexColor;
            out vec4 vtxColor;
            uniform mat4 uProjectionViewMatrix;
            void main(void) {
                vtxColor = aVertexColor;
                gl_Position = uProjectionViewMatrix * vec4(aVertexPosition, 1.0);
            }";

        private const string FragmentShaderSource = @"
            #version 330
            in vec4 vtxColor;
            out vec4 outputColor;
            void main(void) {
                outputColor = vtxColor;
            }";

        private readonly int vao;
        private readonly int vbo;
        private readonly int shaderProgram;

        private readonly int vertexCount;

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

        public OctreeDebugRenderer(Octree<T> octree)
        {
            var vertices = new List<float>();
            AddOctreeNode(vertices, octree.Root, 0);

            vertexCount = vertices.Count / 7;
            var stride = sizeof(float) * 7;

            shaderProgram = SetupShaderProgram();
            GL.UseProgram(shaderProgram);

            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Count * sizeof(float), vertices.ToArray(), BufferUsageHint.StaticDraw);

            var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);
            var colorAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexColor");
            GL.EnableVertexAttribArray(colorAttributeLocation);
            GL.VertexAttribPointer(colorAttributeLocation, 4, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            GL.BindVertexArray(0);
        }

        public void Update(float frameTime)
        {
        }

        public void Render(Camera camera)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);
            GL.UseProgram(shaderProgram);

            var projectionViewMatrix = camera.CameraViewMatrix * camera.ProjectionMatrix;
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjectionViewMatrix"), false, ref projectionViewMatrix);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Lines, 0, vertexCount);
            GL.BindVertexArray(0);
            GL.UseProgram(0);
            GL.Disable(EnableCap.Blend);
        }

        private int SetupShaderProgram()
        {
            var shaderProgram = GL.CreateProgram();

            var vertexShader = GL.CreateShader(ShaderType.VertexShader);
            GL.ShaderSource(vertexShader, VertextShaderSource);
            GL.CompileShader(vertexShader);

            GL.GetShader(vertexShader, ShaderParameter.CompileStatus, out var vertextShaderStatus);
            if (vertextShaderStatus != 1)
            {
                GL.GetShaderInfoLog(vertexShader, out var vsInfo);
                throw new Exception($"Error setting up vertex shader : {vsInfo}");
            }

            var fragmentShader = GL.CreateShader(ShaderType.FragmentShader);
            GL.ShaderSource(fragmentShader, FragmentShaderSource);
            GL.CompileShader(fragmentShader);

            GL.GetShader(fragmentShader, ShaderParameter.CompileStatus, out var fragmentShaderStatus);
            if (fragmentShaderStatus != 1)
            {
                GL.GetShaderInfoLog(fragmentShader, out var fsInfo);
                throw new Exception($"Error setting up fragment shader : {fsInfo}");
            }

            GL.AttachShader(shaderProgram, vertexShader);
            GL.AttachShader(shaderProgram, fragmentShader);
            GL.LinkProgram(shaderProgram);

            GL.GetProgram(shaderProgram, GetProgramParameterName.LinkStatus, out var programStatus);
            if (programStatus != 1)
            {
                GL.GetProgramInfoLog(shaderProgram, out var programInfo);
                throw new Exception($"Error linking shader program: {programInfo}");
            }

            return shaderProgram;
        }

        private float[] GenerateGridVertexBuffer(float cellWidth, int gridWidthInCells)
        {
            var gridVertices = new List<float>();

            var width = cellWidth * gridWidthInCells;

            for (var i = 0; i <= gridWidthInCells; i++)
            {
                gridVertices.AddRange(new[] { width, i * cellWidth, 0 });
                gridVertices.AddRange(new[] { -width, i * cellWidth, 0 });
            }

            for (var i = 1; i <= gridWidthInCells; i++)
            {
                gridVertices.AddRange(new[] { width, -i * cellWidth, 0 });
                gridVertices.AddRange(new[] { -width, -i * cellWidth, 0 });
            }

            for (var i = 0; i <= gridWidthInCells; i++)
            {
                gridVertices.AddRange(new[] { i * cellWidth, width, 0 });
                gridVertices.AddRange(new[] { i * cellWidth, -width, 0 });
            }

            for (var i = 1; i <= gridWidthInCells; i++)
            {
                gridVertices.AddRange(new[] { -i * cellWidth, width, 0 });
                gridVertices.AddRange(new[] { -i * cellWidth, -width, 0 });
            }

            return gridVertices.ToArray();
        }
    }
}

using System;
using System.Collections.Generic;
using GUI.Types.Renderer;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleGrid : IRenderer
    {
        private const string VertextShaderSource = @"
            attribute vec3 aVertexPosition;

            uniform mat4 uProjectionViewMatrix;

            void main(void) {
                gl_Position = uProjectionViewMatrix * vec4(aVertexPosition, 1.0);
            }";

        private const string FragmentShaderSource = @"
            precision mediump float;

            void main(void) {
                gl_FragColor = vec4(1.0, 1.0, 1.0, 1.0);
            }";

        private readonly int vao;
        private readonly int vbo;
        private readonly int shaderProgram;

        private readonly int vertexCount;

        public ParticleGrid(float cellWidth, int gridWidthInCells)
        {
            var vertices = GenerateGridVertexBuffer(cellWidth, gridWidthInCells);

            vertexCount = vertices.Length / 3; // Number of vertices in our buffer

            shaderProgram = SetupShaderProgram();

            GL.UseProgram(shaderProgram);

            // Create and bind VAO
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var positionAttributeLocation = GL.GetAttribLocation(shaderProgram, "aVertexPosition");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0); // Unbind VAO
        }

        public void Update(float frameTime)
        {
            // not required
        }

        public void Render(Camera camera)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.UseProgram(shaderProgram);

            var projectionViewMatrix = camera.CameraViewMatrix * camera.ProjectionMatrix;
            GL.UniformMatrix4(GL.GetUniformLocation(shaderProgram, "uProjectionViewMatrix"), false, ref projectionViewMatrix);

            GL.BindVertexArray(vao);
            GL.EnableVertexAttribArray(0);

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

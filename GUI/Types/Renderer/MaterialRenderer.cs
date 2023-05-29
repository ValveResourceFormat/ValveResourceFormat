using System;
using System.Collections.Generic;
using System.Linq;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal class MaterialRenderer : IRenderer
    {
        private readonly RenderMaterial material;
        private readonly Shader shader;
        private readonly int quadVao;

        public AABB BoundingBox => new(-1, -1, -1, 1, 1, 1);

        public MaterialRenderer(VrfGuiContext vrfGuiContext, Resource resource)
        {
            material = vrfGuiContext.MaterialLoader.LoadMaterial(resource);
            shader = material.Shader;
            quadVao = SetupQuadBuffer();
        }

        private int SetupQuadBuffer()
        {
            GL.UseProgram(shader.Program);

            // Create and bind VAO
            var vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            var vertices = new[]
            {
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                -1.0f, -1.0f, 0.0f,  0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                -1.0f, 1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   0.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                1.0f, -1.0f, 0.0f,   0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 1.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
                // position          ; normal                  ; texcoord    ; tangent                 ; blendindices            ; blendweight
                1.0f, 1.0f, 0.0f,    0.0f, 0.0f, 0.0f, 1.0f,   1.0f, 0.0f,   1.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,   0.0f, 0.0f, 0.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var attributes = new List<(string Name, int Size)>
            {
                ("vPOSITION", 3),
                ("vNORMAL", 4),
                ("vTEXCOORD", 2),
                ("vTANGENT", 4),
                ("vBLENDINDICES", 4),
                ("vBLENDWEIGHT", 4),
            };
            var stride = sizeof(float) * attributes.Sum(x => x.Size);
            var offset = 0;

            foreach (var (Name, Size) in attributes)
            {
                var attributeLocation = GL.GetAttribLocation(shader.Program, Name);
                GL.EnableVertexAttribArray(attributeLocation);
                GL.VertexAttribPointer(attributeLocation, Size, VertexAttribPointerType.Float, false, stride, offset);
                offset += sizeof(float) * Size;
            }

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            GL.UseProgram(shader.Program);
            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            var uniformLocation = shader.GetUniformLocation("m_vTintColorSceneObject");
            if (uniformLocation > -1)
            {
                GL.Uniform4(uniformLocation, Vector4.One);
            }

            uniformLocation = shader.GetUniformLocation("m_vTintColorDrawCall");
            if (uniformLocation > -1)
            {
                GL.Uniform3(uniformLocation, Vector3.One);
            }

            var identity = Matrix4.Identity;

            uniformLocation = shader.GetUniformLocation("uProjectionViewMatrix");
            if (uniformLocation > -1)
            {
                GL.UniformMatrix4(uniformLocation, false, ref identity);
            }

            uniformLocation = shader.GetUniformLocation("transform");
            if (uniformLocation > -1)
            {
                GL.UniformMatrix4(uniformLocation, false, ref identity);
            }

            material.Render(shader);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public void Update(float frameTime)
        {
            throw new NotImplementedException();
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    class MaterialRenderer : SceneNode
    {
        private RenderMaterial material;
        private Resource resource;
        private readonly int quadVao;

        public AABB LocalBoundingBox => new(-1, -1, -1, 1, 1, 1);

        public MaterialRenderer(VrfGuiContext vrfGuiContext, Resource resource)
        {
            this.resource = resource;
            material = scene.GuiContext.MaterialLoader.LoadMaterial(resource);
            quadVao = SetupSquareQuadBuffer(material.Shader);
            Transform = Matrix4x4.Identity;
            LocalBoundingBox = new AABB(-Vector3.One, Vector3.One);
        }

        public static int SetupSquareQuadBuffer(Shader shader)
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
                if (attributeLocation > -1)
                {
                    GL.EnableVertexAttribArray(attributeLocation);
                    GL.VertexAttribPointer(attributeLocation, Size, VertexAttribPointerType.Float, false, stride, offset);
                }
                offset += sizeof(float) * Size;
            }

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        public override void Render(Scene.RenderContext context)
        {
            var shader = material.Shader;

            GL.UseProgram(shader.Program);
            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            shader.SetUniform4("m_vTintColorSceneObject", Vector4.One);
            shader.SetUniform3("m_vTintColorDrawCall", Vector3.One);
            shader.SetUniform4x4("uProjectionViewMatrix", Matrix4x4.Identity);
            shader.SetUniform4x4("transform", Matrix4x4.Identity);

            material.Render(shader);

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            material.PostRender();

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
            //
        }

        public override void SetRenderMode(string mode)
        {
            material = Scene.GuiContext.MaterialLoader.LoadMaterial(resource);
        }

        public override IEnumerable<string> GetSupportedRenderModes() => material.Shader.RenderModes;
    }
}

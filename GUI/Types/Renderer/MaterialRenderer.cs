using System;
using GUI.Utils;
using OpenTK;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal class MaterialRenderer : IRenderer
    {
        private readonly RenderMaterial material;
        private readonly Shader shader;
        private readonly int quadVao;

        public MaterialRenderer(RenderMaterial renderMaterial, VrfGuiContext vrfGuiContext)
        {
            material = renderMaterial;
            shader = vrfGuiContext.ShaderLoader.LoadPlaneShader(material.Material.ShaderName);
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
                // position       ; normal          ; texcoord  ; tangent
                -1.0f, -1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 1.0f, 1.0f, 0.0f, 0.0f,
                // position      ; normal          ; texcoord  ; tangent
                -1.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f, 1.0f, 0.0f, 0.0f,
                // position      ; normal          ; texcoord  ; tangent
                1.0f, -1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 1.0f, 1.0f, 0.0f, 0.0f,
                // position     ; normal          ; texcoord  ; tangent
                1.0f, 1.0f, 0.0f, 0.0f, 0.0f, 1.0f, 1.0f, 0.0f, 1.0f, 0.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var stride = sizeof(float) * 11;

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "vPOSITION");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, 0);

            var normalAttributeLocation = GL.GetAttribLocation(shader.Program, "vNORMAL");
            GL.EnableVertexAttribArray(normalAttributeLocation);
            GL.VertexAttribPointer(normalAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, sizeof(float) * 3);

            var texCoordAttributeLocation = GL.GetAttribLocation(shader.Program, "vTEXCOORD");
            GL.EnableVertexAttribArray(texCoordAttributeLocation);
            GL.VertexAttribPointer(texCoordAttributeLocation, 2, VertexAttribPointerType.Float, false, stride, sizeof(float) * 6);

            var tangentAttributeLocation = GL.GetAttribLocation(shader.Program, "vTANGENT");
            GL.EnableVertexAttribArray(tangentAttributeLocation);
            GL.VertexAttribPointer(tangentAttributeLocation, 3, VertexAttribPointerType.Float, false, stride, sizeof(float) * 8);

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        public void Render(Camera camera)
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

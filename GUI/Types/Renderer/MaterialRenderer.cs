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
            shader = vrfGuiContext.ShaderLoader.LoadShader(material.Material.ShaderName, material.Material.GetShaderArguments());
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
                -1.0f, -1.0f, 0.0f,
                -1.0f, 1.0f, 0.0f,
                1.0f, -1.0f, 0.0f,
                1.0f, 1.0f, 0.0f,
            };

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "vPOSITION");
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0); // Unbind VAO

            return vao;
        }

        public void Render(Camera camera)
        {
            GL.UseProgram(shader.Program);
            GL.BindVertexArray(quadVao);
            GL.EnableVertexAttribArray(0);

            int uniformLocation;
            var textureUnit = 1;

            uniformLocation = shader.GetUniformLocation("vLightPosition");
            GL.Uniform3(uniformLocation, camera.Location);

            uniformLocation = shader.GetUniformLocation("vEyePosition");
            GL.Uniform3(uniformLocation, camera.Location);

            uniformLocation = shader.GetUniformLocation("projection");
            var matrix = camera.ProjectionMatrix;
            GL.UniformMatrix4(uniformLocation, false, ref matrix);

            uniformLocation = shader.GetUniformLocation("modelview");
            matrix = camera.CameraViewMatrix;
            GL.UniformMatrix4(uniformLocation, false, ref matrix);

            uniformLocation = shader.GetUniformLocation("bAnimated");
            if (uniformLocation != -1)
            {
                GL.Uniform1(uniformLocation, 0.0f);
            }

            foreach (var texture in material.Textures)
            {
                uniformLocation = shader.GetUniformLocation(texture.Key);

                if (uniformLocation > -1)
                {
                    GL.ActiveTexture(TextureUnit.Texture0 + textureUnit);
                    GL.BindTexture(TextureTarget.Texture2D, texture.Value);
                    GL.Uniform1(uniformLocation, textureUnit);

                    textureUnit++;
                }
            }

            foreach (var param in material.Material.FloatParams)
            {
                uniformLocation = shader.GetUniformLocation(param.Key);

                if (uniformLocation > -1)
                {
                    GL.Uniform1(uniformLocation, param.Value);
                }
            }

            foreach (var param in material.Material.VectorParams)
            {
                uniformLocation = shader.GetUniformLocation(param.Key);

                if (uniformLocation > -1)
                {
                    GL.Uniform4(uniformLocation, new Vector4(param.Value.X, param.Value.Y, param.Value.Z, param.Value.W));
                }
            }

            GL.DrawArrays(PrimitiveType.TriangleStrip, 0, 4);

            GL.BindVertexArray(0);
            GL.UseProgram(0);
        }

        public void Update(float frameTime)
        {
            throw new NotImplementedException();
        }
    }
}

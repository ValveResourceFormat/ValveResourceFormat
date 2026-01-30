using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Renders an infinite reference grid on the XY plane.
    /// </summary>
    public class InfiniteGrid
    {
        private readonly int vao;
        private readonly Shader shader;

        public InfiniteGrid(Scene scene)
        {
            var vertices = new[]
            {
                -1f, 1f,
                -1f, -1f,
                1f, 1f,
                1f, -1f,
                1f, 1f,
                -1f, -1f,
            };

            shader = scene.RendererContext.ShaderLoader.LoadShader("vrf.grid");

            // Create VAO
            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out int buffer);
            GL.NamedBufferData(buffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.VertexArrayVertexBuffer(vao, 0, buffer, 0, sizeof(float) * 2);
            //SLANG: needed hardcoding because the input name doesn't exist anymore
            var attributeLocation = 0; // GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexArrayAttrib(vao, attributeLocation);
            GL.VertexArrayAttribFormat(vao, attributeLocation, 2, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vao, attributeLocation, 0);

#if DEBUG
            var vaoLabel = nameof(InfiniteGrid);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, vaoLabel.Length, vaoLabel);
#endif
        }

        public void Render(Scene.RenderContext context)
        {
            GL.Enable(EnableCap.Blend);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            shader.Use();
            GL.BindVertexArray(vao);

            foreach (var (slot, name, texture) in context.Textures)
            {
                shader.SetTexture((int)slot, name, texture);
            }

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
        }
    }
}

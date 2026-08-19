using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.CompiledShader;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Renders an infinite reference grid on the XY plane.
    /// </summary>
    public class InfiniteGrid
    {
        private readonly int vao;
        private readonly Shader shader;
        private readonly RenderStateTracker renderState;

        /// <summary>Initializes the grid geometry and loads the grid shader.</summary>
        /// <param name="scene">Scene providing the renderer context.</param>
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

            shader = scene.RendererContext.ShaderLoader.LoadShader("grid");
            renderState = scene.RendererContext.RenderState;

            GL.CreateBuffers(1, out int buffer);

#if DEBUG
            var vaoLabel = nameof(InfiniteGrid);
            GL.ObjectLabel(ObjectLabelIdentifier.Buffer, buffer, vaoLabel.Length, vaoLabel);
#endif

            GL.NamedBufferData(buffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            var format = new VertexInputLayout(sizeof(float) * 2, new VertexAttribute(VertexSlot.Position, DXGI_FORMAT.R32G32_FLOAT));
            vao = format.CreateVertexArray(nameof(InfiniteGrid), buffer);
        }

        /// <summary>Renders the infinite grid for the current frame.</summary>
        public void Render()
        {
            using var _ = renderState.Scope(blend: true);

            shader.Use();
            VertexArray.Bind(vao, shader);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);
        }
    }
}

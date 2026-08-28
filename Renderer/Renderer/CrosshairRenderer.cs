using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Draws the classic static crosshair at the center of the viewport, for walk mode.
    /// </summary>
    public class CrosshairRenderer
    {
        // The game's default crosshair settings, except shorter bars and no center dot
        private const float BarSize = 3f;
        private const float BarThickness = 0.5f;
        private const int Gap = 4 + 1; // fixed base gap plus the gap setting
        private const int Outline = 1;
        private static readonly Color32 LineColor = new(50, 250, 50, 200);
        private static readonly Color32 OutlineColor = new(0, 0, 0, 200);

        private readonly RendererContext RendererContext;

        private Shader? shader;
        private int bufferHandle;
        private int vao;

        // 4 bars, each with an outline rect behind it
        private const int VertexCount = 8 * 4;
        private Vector2 builtForWindowSize;

        /// <summary>Initializes the crosshair renderer.</summary>
        /// <param name="rendererContext">Renderer context for loading shaders.</param>
        public CrosshairRenderer(RendererContext rendererContext)
        {
            RendererContext = rendererContext;
        }

        private static void AddRect(Span<SimpleVertex> vertices, ref int i, int x0, int y0, int x1, int y1, Color32 color)
        {
            vertices[i++] = new SimpleVertex(new Vector3(x0, y0, 0f), color);
            vertices[i++] = new SimpleVertex(new Vector3(x0, y1, 0f), color);
            vertices[i++] = new SimpleVertex(new Vector3(x1, y1, 0f), color);
            vertices[i++] = new SimpleVertex(new Vector3(x1, y0, 0f), color);
        }

        private static void AddBar(Span<SimpleVertex> vertices, ref int i, int x0, int y0, int x1, int y1)
        {
            AddRect(vertices, ref i, x0 - Outline, y0 - Outline, x1 + Outline, y1 + Outline, OutlineColor);
            AddRect(vertices, ref i, x0, y0, x1, y1, LineColor);
        }

        /// <summary>Draws the crosshair for the current frame.</summary>
        /// <param name="camera">Camera providing the viewport dimensions.</param>
        public void Render(Camera camera)
        {
            if (shader == null)
            {
                shader = RendererContext.ShaderLoader.LoadShader("crosshair");
                bufferHandle = GraphicsDevice.CreateBuffer(nameof(CrosshairRenderer));
                vao = SimpleVertex.InputLayout.CreateVertexArray(nameof(CrosshairRenderer), bufferHandle, RendererContext.MeshBufferCache.QuadIndices.GLHandle);
            }

            if (builtForWindowSize != camera.WindowSize)
            {
                BuildVertices(camera.WindowSize);
            }

            using var _ = new GLDebugGroup("Crosshair Render");

            PerfStats.Active.SuspendTriangleCounter();

            using var renderState = GraphicsContext.RenderState.Scope(depthTest: false, blend: true);

            shader.Use();
            shader.SetUniform4x4("transform", Matrix4x4.CreateOrthographicOffCenter(0f, camera.WindowSize.X, camera.WindowSize.Y, 0f, -100f, 100f));

            VertexArray.Bind(vao, shader);
            GL.DrawElements(PrimitiveType.Triangles, VertexCount / 4 * 6, DrawElementsType.UnsignedShort, 0);

            PerfStats.Active.ResumeTriangleCounter();
        }

        private void BuildVertices(Vector2 windowSize)
        {
            builtForWindowSize = windowSize;

            // Bar size and thickness are authored in 480-line units, but the gap is not resolution scaled
            var scale = windowSize.Y / 480f;
            var size = (int)MathF.Round(BarSize * scale);
            var thickness = Math.Max(1, (int)MathF.Round(BarThickness * scale));

            var centerX = (int)windowSize.X / 2;
            var centerY = (int)windowSize.Y / 2;

            // Even thicknesses sit half a pixel off center rather than going lopsided
            var xLo = centerX - thickness / 2;
            var xHi = xLo + thickness;
            var yLo = centerY - thickness / 2;
            var yHi = yLo + thickness;

            using var vertexBuffer = new RentedFloatBuffer<SimpleVertex>(VertexCount);
            var vertices = vertexBuffer.Span;

            var i = 0;
            AddBar(vertices, ref i, xLo - Gap - size, yLo, xLo - Gap, yHi);
            AddBar(vertices, ref i, xHi + Gap, yLo, xHi + Gap + size, yHi);
            AddBar(vertices, ref i, xLo, yLo - Gap - size, xHi, yLo - Gap);
            AddBar(vertices, ref i, xLo, yHi + Gap, xHi, yHi + Gap + size);

            GL.NamedBufferData(bufferHandle, VertexCount * SimpleVertex.InputLayout.Stride, vertexBuffer.FloatArray, BufferUsageHint.StaticDraw);
        }
    }
}

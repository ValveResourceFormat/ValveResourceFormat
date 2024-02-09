using GUI.Types.Renderer;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.ParticleRenderer
{
    internal class InfiniteGrid : SceneNode
    {
        private readonly int vao;
        private Shader shader;

        public InfiniteGrid(Scene scene) : base(scene)
        {
            var vertices = new[]
            {
                1f, 1f, 0f,
                -1f, -1f, 0f,
                -1f, 1f, 0f,
                -1f, -1f, 0f,
                1f, 1f, 0f,
                1f, -1f, 0f,
            };

            ReloadShader();

            // Create VAO
            GL.CreateVertexArrays(1, out vao);
            GL.CreateBuffers(1, out int buffer);
            GL.NamedBufferData(buffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);
            GL.VertexArrayVertexBuffer(vao, 0, buffer, 0, sizeof(float) * 3);

            var attributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexArrayAttrib(vao, attributeLocation);
            GL.VertexArrayAttribFormat(vao, attributeLocation, 3, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vao, attributeLocation, 0);

#if DEBUG
            var vaoLabel = nameof(InfiniteGrid);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, vaoLabel.Length, vaoLabel);
#endif
        }

        public override void Update(Scene.UpdateContext context)
        {
            // not required
        }

        public override void Render(Scene.RenderContext context)
        {
            GL.Enable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.OneMinusSrcAlpha);

            GL.UseProgram(shader.Program);
            GL.BindVertexArray(vao);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }

        public void ReloadShader()
        {
            shader = Scene.GuiContext.ShaderLoader.LoadShader("vrf.grid");
        }
    }
}

using System.Numerics;
using GUI.Types.Renderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.ParticleRenderer
{
    internal class ParticleGrid : IRenderer
    {
        private readonly int vao;
        private Shader shader;
        private VrfGuiContext guiContext;

        public AABB LocalBoundingBox { get; }

        public ParticleGrid(float width, VrfGuiContext guiContext)
        {
            var center = width / 2f;
            LocalBoundingBox = new AABB(new Vector3(-center, -center, 0), new Vector3(center, center, 0));

            var vertices = new[]
            {
                -width,  width, 0f,
                -width, -width, 0f,
                width, -width, 0f,
                width, -width, 0f,
                width, width, 0f,
                -width, width, 0f,
            };

            this.guiContext = guiContext;
            ReloadShader();

            // Create and bind VAO
            vao = GL.GenVertexArray();
            GL.BindVertexArray(vao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);

            GL.BufferData(BufferTarget.ArrayBuffer, vertices.Length * sizeof(float), vertices, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);

            var positionAttributeLocation = GL.GetAttribLocation(shader.Program, "aVertexPosition");
            GL.EnableVertexAttribArray(positionAttributeLocation);
            GL.VertexAttribPointer(positionAttributeLocation, 3, VertexAttribPointerType.Float, false, sizeof(float) * 3, 0);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
        }

        public void Update(float frameTime)
        {
            // not required
        }

        public void Render(Camera camera, RenderPass renderPass)
        {
            GL.Enable(EnableCap.Blend);
            GL.Disable(EnableCap.CullFace);
            GL.BlendFunc(BlendingFactor.SrcAlpha, BlendingFactor.DstAlpha);

            GL.UseProgram(shader.Program);
            GL.BindVertexArray(vao);
            GL.EnableVertexAttribArray(0);

            shader.SetUniform4x4("uProjectionViewMatrix", camera.ViewProjectionMatrix);

            GL.DrawArrays(PrimitiveType.Triangles, 0, 6);

            GL.UseProgram(0);
            GL.BindVertexArray(0);
            GL.Disable(EnableCap.Blend);
            GL.Enable(EnableCap.CullFace);
        }

        public void ReloadShader()
        {
            shader = guiContext.ShaderLoader.LoadShader("vrf.grid");
        }
    }
}

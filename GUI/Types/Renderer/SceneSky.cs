using System.Numerics;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SceneSky : SceneNode
    {
        public Vector3 Tint { get; set; } = Vector3.One;
        public RenderMaterial Material { get; set; }
        private readonly int boxVao;
        private readonly float[] boxTriangles = [
#pragma warning disable format
            // positions
            -1.0f,  1.0f, -1.0f,
            -1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f, -1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,

            -1.0f, -1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f, -1.0f,  1.0f,
            -1.0f, -1.0f,  1.0f,

            -1.0f,  1.0f, -1.0f,
            1.0f,  1.0f, -1.0f,
            1.0f,  1.0f,  1.0f,
            1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f,  1.0f,
            -1.0f,  1.0f, -1.0f,

            -1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
            1.0f, -1.0f, -1.0f,
            1.0f, -1.0f, -1.0f,
            -1.0f, -1.0f,  1.0f,
            1.0f, -1.0f,  1.0f
#pragma warning restore format
        ];

        public SceneSky(Scene scene) : base(scene)
        {
            boxVao = GL.GenVertexArray();
            GL.BindVertexArray(boxVao);

            var vbo = GL.GenBuffer();
            GL.BindBuffer(BufferTarget.ArrayBuffer, vbo);
            GL.BufferData(BufferTarget.ArrayBuffer, boxTriangles.Length * sizeof(float), boxTriangles, BufferUsageHint.StaticDraw);

            GL.EnableVertexAttribArray(0);
            GL.VertexAttribPointer(0, 3, VertexAttribPointerType.Float, false, 0, 0);

            GL.BindVertexArray(0);
        }

        public override void Update(Scene.UpdateContext context)
        {
        }

        public override void Render(Scene.RenderContext context)
        {
            GL.DepthFunc(DepthFunction.Gequal);

            GL.UseProgram(Material.Shader.Program);
            GL.BindVertexArray(boxVao);

            Material.Render();
            Material.Shader.SetUniform3("m_vTint", Tint);
            Material.Shader.SetUniform4x4("g_matSkyRotation", Transform);
            GL.DrawArrays(PrimitiveType.Triangles, 0, boxTriangles.Length / 3);
            Material.PostRender();

            GL.UseProgram(0);
            GL.BindVertexArray(0);

            GL.DepthFunc(DepthFunction.Greater);
        }

        public override void SetRenderMode(string mode)
        {
            using var mat = Scene.GuiContext.LoadFileCompiled(Scene.Sky?.Material.Material.Name);
            Material = Scene.GuiContext.MaterialLoader.LoadMaterial(mat);
        }
    }
}

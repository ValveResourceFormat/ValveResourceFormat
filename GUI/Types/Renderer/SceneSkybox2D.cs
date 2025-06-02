using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SceneSkybox2D
    {
        public Vector3 Tint { get; init; } = Vector3.One;
        public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
        public RenderMaterial Material { get; }
        private readonly int vao;

        public SceneSkybox2D(RenderMaterial material)
        {
            Material = material;
            GL.CreateVertexArrays(1, out vao);
        }

        public void Render()
        {
            GL.DepthFunc(DepthFunction.Equal);

            Material.Shader.Use();
            Material.Render();
            Material.Shader.SetUniform3("m_vTint", Tint);
            Material.Shader.SetUniform4x4("g_matSkyRotation", Transform);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            Material.PostRender();

            GL.UseProgram(0);
            GL.DepthFunc(DepthFunction.Greater);
        }
    }
}

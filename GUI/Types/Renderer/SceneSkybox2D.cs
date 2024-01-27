using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SceneSkybox2D
    {
        public Vector3 Tint { get; init; } = Vector3.One;
        public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
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

        public SceneSkybox2D()
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

        public void Render()
        {
            GL.DepthFunc(DepthFunction.Equal);

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
    }
}

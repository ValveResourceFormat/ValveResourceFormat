using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    class SceneSkybox2D
    {
        public Vector3 Tint { get; init; } = Vector3.One;
        public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;
        public RenderMaterial Material { get; }

        private readonly int vaoHandle;
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

        public SceneSkybox2D(RenderMaterial material)
        {
            Material = material;

            GL.CreateVertexArrays(1, out vaoHandle);
            GL.CreateBuffers(1, out int vboHandle);
            GL.VertexArrayVertexBuffer(vaoHandle, 0, vboHandle, 0, sizeof(float) * 3);

            var attributeLocation = GL.GetAttribLocation(material.Shader.Program, "aVertexPosition");
            GL.EnableVertexArrayAttrib(vaoHandle, attributeLocation);
            GL.VertexArrayAttribFormat(vaoHandle, attributeLocation, 3, VertexAttribType.Float, false, 0);
            GL.VertexArrayAttribBinding(vaoHandle, attributeLocation, 0);

            GL.NamedBufferData(vboHandle, boxTriangles.Length * sizeof(float), boxTriangles, BufferUsageHint.StaticDraw);

#if DEBUG
            var vaoLabel = nameof(SceneSkybox2D);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vaoHandle, vaoLabel.Length, vaoLabel);
#endif
        }

        public void Render()
        {
            GL.DepthFunc(DepthFunction.Equal);

            GL.UseProgram(Material.Shader.Program);
            GL.BindVertexArray(vaoHandle);

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

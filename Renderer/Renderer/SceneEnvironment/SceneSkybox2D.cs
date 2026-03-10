using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.SceneEnvironment
{
    /// <summary>
    /// Renders a 2D skybox using a fullscreen cube.
    /// </summary>
    public class SceneSkybox2D
    {
        /// <summary>Gets the color tint multiplied with the skybox texture during rendering.</summary>
        public Vector3 Tint { get; init; } = Vector3.One;

        /// <summary>Gets the rotation transform applied to the skybox cube.</summary>
        public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;

        /// <summary>Gets the material used to render the skybox.</summary>
        public RenderMaterial Material { get; }
        private readonly int vao;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSkybox2D"/> class with the given material.
        /// </summary>
        /// <param name="material">The material to use for skybox rendering.</param>
        public SceneSkybox2D(RenderMaterial material)
        {
            Material = material;
            GL.CreateVertexArrays(1, out vao);

#if DEBUG
            var vaoLabel = nameof(SceneSkybox2D);
            GL.ObjectLabel(ObjectLabelIdentifier.VertexArray, vao, vaoLabel.Length, vaoLabel);
#endif
        }

        /// <summary>Renders the skybox using a fullscreen 36-vertex cube draw call.</summary>
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

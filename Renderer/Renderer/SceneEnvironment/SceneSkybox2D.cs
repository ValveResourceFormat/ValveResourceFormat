using OpenTK.Graphics.OpenGL;

namespace ValveResourceFormat.Renderer.SceneEnvironment
{
    /// <summary>How a sky is fogged.</summary>
    public enum SkyFogType
    {
        /// <summary>Not at all.</summary>
        None,

        /// <summary>By the fog of the map, as far away as the far plane.</summary>
        Distance,

        /// <summary>By the angle above or below the horizon, toward the color of the fog of the map.</summary>
        Angular,
    }

    /// <summary>
    /// Renders a 2D skybox using a fullscreen cube.
    /// </summary>
    public class SceneSkybox2D
    {
        /// <summary>Gets the color tint multiplied with the skybox texture during rendering.</summary>
        public Vector3 Tint { get; init; } = Vector3.One;

        /// <summary>Gets the rotation transform applied to the skybox cube.</summary>
        public Matrix4x4 Transform { get; init; } = Matrix4x4.Identity;

        /// <summary>Gets how the sky is fogged.</summary>
        public SkyFogType FogType { get; init; }

        /// <summary>
        /// Gets the angles in degrees an angular fog fades over: below the horizon where it starts and where
        /// it is full (X, Y), and above it where it is full and where it ends (Z, W).
        /// </summary>
        public Vector4 AngularFog
        {
            get;
            init
            {
                field = value;
                AngularFogMinMaxDots = new Vector4(ToHeight(value.X), ToHeight(value.Y), ToHeight(value.Z), ToHeight(value.W));
            }
        }

        private Vector4 AngularFogMinMaxDots;

        /// <summary>Gets or sets the material used to render the skybox.</summary>
        public RenderMaterial Material { get; set; }
        private readonly int vao;

        /// <summary>
        /// Initializes a new instance of the <see cref="SceneSkybox2D"/> class with the given material.
        /// </summary>
        /// <param name="material">The material to use for skybox rendering.</param>
        public SceneSkybox2D(RenderMaterial material)
        {
            Material = material;
            vao = GraphicsDevice.CreateVertexArray(nameof(SceneSkybox2D));
        }

        /// <summary>
        /// Releases owned GPU resources.
        /// </summary>
        public virtual void Delete()
        {
            GL.DeleteVertexArray(vao);
        }

        /// <summary>Renders the skybox using a fullscreen 36-vertex cube draw call.</summary>
        /// <param name="fog">The fog of the map the sky is fogged with, or <see langword="null"/> to draw it without fog.</param>
        /// <param name="camera">The camera of the view, whose far plane a sky fogged by distance is at.</param>
        public void Render(LinearFog? fog = null, Camera? camera = null)
        {
            using var _ = GraphicsContext.RenderState.Scope(depthFunc: RsComparison.Equal);

            Material.Shader.Use();
            Material.Render();
            Material.SetUniform("g_vTint", Tint);
            Material.SetUniform("g_matSkyRotation", Transform);
            SetFogUniforms(fog, camera);

            GL.BindVertexArray(vao);
            GL.DrawArrays(PrimitiveType.Triangles, 0, 36);
            Material.PostRender();
        }

        private void SetFogUniforms(LinearFog? fog, Camera? camera)
        {
            var fogType = fog == null || camera == null ? SkyFogType.None : FogType;

            Material.SetUniform("g_nSkyFogType", (long)fogType);

            if (fog is not { } linearFog || camera == null || fogType == SkyFogType.None)
            {
                return;
            }

            var skyDistance = linearFog.FarZ > 0f ? MathF.Min(linearFog.FarZ, camera.FarPlane) : camera.FarPlane;

            Material.SetUniform("g_vSkyFogColor", linearFog.GetColor(camera.Forward));
            Material.SetUniform("g_flSkyDistanceFog", linearFog.GetAmount(skyDistance));

            Material.SetUniform("g_vSkyFogMinMaxDots", AngularFogMinMaxDots);

            // The fog below the horizon is only there while both of its angles are above minus one degree
            Material.SetUniform("g_bSkyMinAngularFog", AngularFog.X > -1f && AngularFog.Y > -1f);
        }

        // Each angle is halved before it is turned into the height of a direction at that angle
        private static float ToHeight(float degrees) => MathF.Sin(degrees * MathF.PI / 360f);
    }
}

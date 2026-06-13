
namespace ValveResourceFormat.Renderer.SceneEnvironment
{
    /// <summary>
    /// Default scene background with configurable solid or gradient rendering.
    /// </summary>
    public class SceneBackground : SceneSkybox2D
    {
        /// <summary>
        /// Initializes a new instance of the <see cref="SceneBackground"/> class using the background shader.
        /// </summary>
        /// <param name="scene">The scene this background belongs to.</param>
        public SceneBackground(Scene scene)
            : base(new RenderMaterial(scene.RendererContext.ShaderLoader.LoadShader("vrf.background")))
        {
        }

        /// <summary>
        /// Enables or disables the light background rendering mode.
        /// </summary>
        /// <param name="enabled">Whether to enable the light background.</param>
        public void SetLightBackground(bool enabled)
        {
            Material.Material.IntParams["g_bShowLightBackground"] = enabled ? 1 : 0;
        }

        /// <summary>
        /// Enables or disables the solid background rendering mode.
        /// </summary>
        /// <param name="enabled">Whether to enable the solid background.</param>
        public void SetSolidBackground(bool enabled)
        {
            Material.Material.IntParams["g_bShowSolidBackground"] = enabled ? 1 : 0;
        }
    }
}

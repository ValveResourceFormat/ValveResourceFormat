
namespace ValveResourceFormat.Renderer
{
    /// <summary>
    /// Default scene background with configurable solid or gradient rendering.
    /// </summary>
    public class SceneBackground : SceneSkybox2D
    {
        public SceneBackground(Scene scene)
            : base(new RenderMaterial(scene.RendererContext.ShaderLoader.LoadShader("vrf.background")))
        {
        }

        public void SetLightBackground(bool enabled)
        {
            Material.Material.IntParams["g_bShowLightBackground"] = enabled ? 1 : 0;
        }

        public void SetSolidBackground(bool enabled)
        {
            Material.Material.IntParams["g_bShowSolidBackground"] = enabled ? 1 : 0;
        }
    }
}

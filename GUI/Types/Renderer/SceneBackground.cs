
namespace GUI.Types.Renderer
{
    public class SceneBackground : SceneSkybox2D
    {
        public SceneBackground(Scene scene)
            : base(new RenderMaterial(scene.GuiContext.ShaderLoader.LoadShader("vrf.background")))
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

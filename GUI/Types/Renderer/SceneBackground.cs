
namespace GUI.Types.Renderer
{
    internal class SceneBackground : SceneSkybox2D
    {
        public SceneBackground(Scene scene)
            : base(new RenderMaterial(scene.GuiContext.ShaderLoader.LoadShader("vrf.background")))
        {
        }

        public void SetLightBackground(bool enabled)
        {
            Material.Material.IntParams["g_bShowLightBackground"] = enabled ? 1 : 0;
        }
    }
}

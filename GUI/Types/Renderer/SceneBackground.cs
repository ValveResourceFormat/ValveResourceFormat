
namespace GUI.Types.Renderer
{
    internal class SceneBackground : SceneSkybox2D
    {
        public SceneBackground(Scene scene)
            : base(new RenderMaterial(scene.GuiContext.ShaderLoader.LoadShader("vrf.background")))
        {
        }
    }
}

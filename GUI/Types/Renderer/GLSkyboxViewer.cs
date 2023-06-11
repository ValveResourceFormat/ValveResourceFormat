using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal class GLSkyboxViewer : GLSceneViewer
    {
        private readonly Resource materialResource;

        public GLSkyboxViewer(VrfGuiContext guiContext, Resource material)
            : base(guiContext)
        {
            materialResource = material;
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();
        }

        protected override void LoadScene()
        {
            Scene.Sky = new SceneSky(Scene)
            {
                Material = GuiContext.MaterialLoader.LoadMaterial(materialResource),
            };
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
        }
    }
}

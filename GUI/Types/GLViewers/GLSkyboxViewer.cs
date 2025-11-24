using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Types.GLViewers
{
    class GLSkyboxViewer : GLSceneViewer
    {
        private readonly Resource materialResource;

        public GLSkyboxViewer(VrfGuiContext guiContext, Resource material)
            : base(guiContext, Frustum.CreateEmpty())
        {
            materialResource = material;
        }

        public override Control InitializeUiControls()
        {
            base.InitializeUiControls();

            AddRenderModeSelectionControl();

            return UiControl;
        }

        protected override void LoadScene()
        {
            Skybox2D = new SceneSkybox2D(GuiContext.MaterialLoader.LoadMaterial(materialResource));
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
        }
    }
}

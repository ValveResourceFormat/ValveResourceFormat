using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    class GLSkyboxViewer : GLSceneViewer
    {
        private readonly Resource materialResource;

        public GLSkyboxViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Resource material)
            : base(vrfGuiContext, rendererContext, Frustum.CreateEmpty())
        {
            materialResource = material;
        }

        protected override void AddUiControls()
        {
            AddRenderModeSelectionControl();

            base.AddUiControls();
        }

        protected override void LoadScene()
        {
            Renderer.Skybox2D = new SceneSkybox2D(Scene.RendererContext.MaterialLoader.LoadMaterial(materialResource));
        }

        protected override void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo)
        {
        }
    }
}

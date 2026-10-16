using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.GLViewers
{
    class GLNavSpaceViewer : GLSceneLayerViewer
    {
        private readonly NavSpaceFile navSpaceFile;

        public GLNavSpaceViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, NavSpaceFile navSpaceFile)
            : base(vrfGuiContext, rendererContext)
        {
            this.navSpaceFile = navSpaceFile;
        }

        protected override string LayersControlName => "Navigation Layers";

        protected override bool IsLayerEnabledByDefault(int index) => index == 0;

        protected override void LoadScene()
        {
            NavSpaceSceneNode.AddToScene(navSpaceFile, Scene);
        }
    }
}

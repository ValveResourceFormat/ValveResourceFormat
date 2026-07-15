using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.GLViewers
{
    class GLNavMeshViewer : GLSceneLayerViewer
    {
        private readonly NavMeshFile navMeshFile;

        public GLNavMeshViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, NavMeshFile navMeshFile)
            : base(vrfGuiContext, rendererContext)
        {
            this.navMeshFile = navMeshFile;
        }

        protected override string LayersControlName => "World Layers";

        protected override void LoadScene()
        {
            NavMeshSceneNode.AddNavNodesToScene(navMeshFile, Scene);
        }
    }
}

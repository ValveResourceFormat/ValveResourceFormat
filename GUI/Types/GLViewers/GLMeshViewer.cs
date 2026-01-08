using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    class GLMeshViewer : GLSingleNodeViewer
    {
        private readonly Mesh mesh;

        public GLMeshViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Mesh mesh) : base(vrfGuiContext, rendererContext)
        {
            this.mesh = mesh;
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            var meshSceneNode = new MeshSceneNode(Scene, mesh, 0);
            Scene.Add(meshSceneNode, false);
        }
    }
}

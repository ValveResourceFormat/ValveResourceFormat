using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    class GLMeshViewer : GLSingleNodeViewer
    {
        private readonly Mesh mesh;

        public GLMeshViewer(VrfGuiContext guiContext, Mesh mesh) : base(guiContext)
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

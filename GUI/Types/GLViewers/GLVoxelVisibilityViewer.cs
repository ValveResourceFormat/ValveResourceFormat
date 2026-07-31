using GUI.Utils;
using ValveResourceFormat.Blocks;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    class GLVoxelVisibilityViewer : GLSceneViewer
    {
        private readonly VoxelVisibility voxelVisibility;

        public GLVoxelVisibilityViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, VoxelVisibility voxelVisibility)
            : base(vrfGuiContext, rendererContext)
        {
            this.voxelVisibility = voxelVisibility;
        }

        protected override void LoadScene()
        {
            var sceneNode = new VisibilitySceneNode(Scene, voxelVisibility)
            {
                LayerName = "Visibility clusters",
            };
            Scene.Add(sceneNode, false);
        }

        protected override void OnPicked(object? sender, PickingResponse pixelInfo)
        {
        }
    }
}

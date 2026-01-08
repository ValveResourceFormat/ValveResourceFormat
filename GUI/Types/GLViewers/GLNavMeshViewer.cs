using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using ValveResourceFormat.Renderer;
using static ValveResourceFormat.Renderer.PickingTexture;

#nullable disable

namespace GUI.Types.GLViewers
{
    class GLNavMeshViewer : GLSceneViewer
    {
        private readonly NavMeshFile navMeshFile;
        private CheckedListBox worldLayersComboBox;

        public GLNavMeshViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, NavMeshFile navMeshFile)
            : base(vrfGuiContext, rendererContext)
        {
            this.navMeshFile = navMeshFile;
        }

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
        }

        protected override void LoadScene()
        {
            NavMeshSceneNode.AddNavNodesToScene(navMeshFile, Scene);
        }

        protected override void AddUiControls()
        {
            AddRenderModeSelectionControl();

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                SetEnabledLayers(new HashSet<string>(worldLayers));
            });

            worldLayersComboBox.BeginUpdate();
            var layerNames = Scene.AllNodes.Select(x => x.LayerName);
            foreach (var layerName in layerNames)
            {
                worldLayersComboBox.Items.Add(layerName, true);
            }
            worldLayersComboBox.EndUpdate();

            base.AddUiControls();
        }

        protected override void OnPicked(object sender, PickingResponse pixelInfo)
        {
        }
    }
}

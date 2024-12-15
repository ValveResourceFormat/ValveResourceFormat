using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using static GUI.Types.Renderer.PickingTexture;

namespace GUI.Types.Renderer
{
    class GLNavMeshViewer : GLSceneViewer
    {
        private readonly NavMeshFile navMeshFile;
        private CheckedListBox worldLayersComboBox;

        public GLNavMeshViewer(VrfGuiContext guiContext, NavMeshFile navMeshFile)
            : base(guiContext)
        {
            this.navMeshFile = navMeshFile;
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                worldLayersComboBox?.Dispose();
            }
        }

        protected override void InitializeControl()
        {
            AddRenderModeSelectionControl();

            worldLayersComboBox = AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                SetEnabledLayers(new HashSet<string>(worldLayers));
            });
        }

        protected override void LoadScene()
        {
            NavMeshSceneNode.AddNavNodesToScene(navMeshFile, Scene);

            worldLayersComboBox.BeginUpdate();
            var layerNames = Scene.AllNodes.Select(x => x.LayerName);
            foreach (var layerName in layerNames)
            {
                worldLayersComboBox.Items.Add(layerName, true);
            }
            worldLayersComboBox.EndUpdate();
        }

        protected override void OnPicked(object sender, PickingResponse pixelInfo)
        {
        }
    }
}

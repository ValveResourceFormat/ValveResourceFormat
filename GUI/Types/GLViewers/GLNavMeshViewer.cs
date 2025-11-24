using System.Linq;
using System.Windows.Forms;
using GUI.Types.Renderer;
using GUI.Utils;
using ValveResourceFormat.NavMesh;
using static GUI.Types.Renderer.PickingTexture;

#nullable disable

namespace GUI.Types.GLViewers
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

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
        }

        public override Control InitializeUiControls()
        {
            base.InitializeUiControls();

            AddRenderModeSelectionControl();

            worldLayersComboBox = UiControl.AddMultiSelection("World Layers", null, (worldLayers) =>
            {
                SetEnabledLayers(new HashSet<string>(worldLayers));
            });

            return UiControl;
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

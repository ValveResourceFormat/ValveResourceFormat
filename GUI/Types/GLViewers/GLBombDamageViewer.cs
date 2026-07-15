using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.SceneNodes;
using ValveResourceFormat.ResourceTypes.GenericData.CS2;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    class GLBombDamageViewer : GLSceneViewer
    {
        private readonly BombDamage bombDamageData;
        private CheckedListBox? worldLayersComboBox;

        public GLBombDamageViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, BombDamage bombDamageData)
            : base(vrfGuiContext, rendererContext)
        {
            this.bombDamageData = bombDamageData;
        }

        public override void Dispose()
        {
            base.Dispose();

            worldLayersComboBox?.Dispose();
        }

        protected override void LoadScene()
        {
            CS2BombDamageSceneNode.AddBakedBombDamageToScene(bombDamageData, Scene);
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            AddRenderModeSelectionControl();

            worldLayersComboBox = UiControl.AddMultiSelection("Bombsites", null, (worldLayers) =>
            {
                SetEnabledLayers([.. worldLayers]);
            });

            worldLayersComboBox.BeginUpdate();
            var layerNames = Scene.AllNodes.Select(static x => x.LayerName).OfType<string>().Distinct().ToList();
            for (var i = 0; i < layerNames.Count; i++)
            {
                // Only enable the first bombsite by default to avoid overlapping visualizations
                worldLayersComboBox.Items.Add(layerNames[i], i == 0);
            }
            worldLayersComboBox.EndUpdate();

            if (layerNames.Count > 0)
            {
                SetEnabledLayers([layerNames[0]]);
            }

            base.AddUiControls();
        }

        protected override void OnPicked(object? sender, PickingResponse pixelInfo)
        {
        }
    }
}

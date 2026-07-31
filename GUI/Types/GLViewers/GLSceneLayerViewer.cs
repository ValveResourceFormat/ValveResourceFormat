using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Base for scene viewers that expose the scene's layers as a multi-selection list.
    /// </summary>
    abstract class GLSceneLayerViewer : GLSceneViewer
    {
        private CheckedListBox? layersListBox;

        protected GLSceneLayerViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext)
            : base(vrfGuiContext, rendererContext)
        {
        }

        /// <summary>
        /// Label of the layer selection control.
        /// </summary>
        protected abstract string LayersControlName { get; }

        /// <summary>
        /// Whether the layer at the given index starts enabled.
        /// </summary>
        protected virtual bool IsLayerEnabledByDefault(int index) => true;

        public override void Dispose()
        {
            base.Dispose();

            layersListBox?.Dispose();
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            AddRenderModeSelectionControl();

            var layerNames = Scene.AllNodes.Select(static x => x.LayerName).OfType<string>().Distinct().ToList();
            var enabledLayers = new HashSet<string>(layerNames.Count);

            layersListBox = UiControl.AddMultiSelection(LayersControlName, (listBox) =>
            {
                for (var i = 0; i < layerNames.Count; i++)
                {
                    var enabled = IsLayerEnabledByDefault(i);
                    listBox.Items.Add(layerNames[i], enabled);

                    if (enabled)
                    {
                        enabledLayers.Add(layerNames[i]);
                    }
                }
            }, (layers) =>
            {
                SetEnabledLayers([.. layers]);
            });

            SetEnabledLayers(enabledLayers);

            base.AddUiControls();
        }

        protected override void OnPicked(object? sender, PickingResponse pixelInfo)
        {
        }
    }
}

using System.Diagnostics;
using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.Renderer;

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

            var listedLayers = Scene.AllNodes.SelectMany(static node => node.Layers).Where(static layer => !layer.IsInternal).Distinct().ToList();
            var enabledLayers = new HashSet<WorldLayer>(listedLayers.Count);

            layersListBox = UiControl.AddMultiSelection<WorldLayer>(LayersControlName, (listBox) =>
            {
                for (var i = 0; i < listedLayers.Count; i++)
                {
                    var enabled = IsLayerEnabledByDefault(i);
                    listBox.Items.Add(listedLayers[i], enabled);

                    if (enabled)
                    {
                        enabledLayers.Add(listedLayers[i]);
                    }
                }
            }, (checkedLayers) =>
            {
                SetEnabledLayers(listedLayers, [.. checkedLayers]);
            });

            SetEnabledLayers(listedLayers, enabledLayers);

            base.AddUiControls();
        }
    }
}

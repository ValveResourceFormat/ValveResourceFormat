using System.Diagnostics;
using System.Linq;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.Renderer;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// Base for scene viewers that expose the scene's layers as checkable items, nested by the slashes in their names.
    /// </summary>
    abstract class GLSceneLayerViewer : GLSceneViewer
    {
        private GLViewerTreeSelectionControl? layersTree;

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

            layersTree?.Dispose();
        }

        protected override void AddUiControls()
        {
            Debug.Assert(UiControl != null);

            AddRenderModeSelectionControl();

            var listedLayers = Scene.AllNodes.SelectMany(static node => node.Layers).Where(static layer => !layer.IsInternal).Distinct().ToList();

            layersTree = AddLayersTree(LayersControlName, listedLayers, layer => IsLayerEnabledByDefault(listedLayers.IndexOf(layer)));

            base.AddUiControls();
        }
    }
}

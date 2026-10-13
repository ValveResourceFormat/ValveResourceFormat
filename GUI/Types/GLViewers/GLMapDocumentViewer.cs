using System.Linq;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.GLViewers
{
    /// <summary>
    /// GL Render control for a source map that is not compiled. The viewer does not own the map.
    /// </summary>
    class GLMapDocumentViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, MapDocument mapDocument) : GLWorldViewer(vrfGuiContext, rendererContext)
    {
        private MapDocumentLoader? loadedMapDocument;

        public override void Dispose()
        {
            base.Dispose();

            loadedMapDocument?.Dispose();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();

            LoadUnbakedLighting();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            loadedMapDocument = new MapDocumentLoader(mapDocument, Scene, Renderer.EntitySystem)
            {
                LoadingProgress = GuiContext.LoadingProgress,
                DefaultEnabledLayers = { HammerEntityVisuals.MarkerLayerName },
            };

            loadedMapDocument.Load();

            // Opens where the map was last looked at in Hammer
            var camera = mapDocument.Root.DefaultCamera;

            if (camera.Position != camera.LookAt)
            {
                Input.Camera.SetLocation(camera.Position);
                Input.Camera.LookAt(camera.LookAt);
            }

            // The world rests as it was authored
            Renderer.EntitySystem.Enabled = false;
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            if (loadedMapDocument == null || worldLayersComboBox == null)
            {
                return;
            }

            var mapLayers = Scene.AllNodes
                .Select(static r => r.LayerName)
                .OfType<string>()
                .Distinct()
                .ToList();

            worldLayersComboBox.BeginUpdate();

            SetAvailableLayers(mapLayers);

            foreach (var mapLayer in loadedMapDocument.DefaultEnabledLayers)
            {
                var checkboxIndex = worldLayersComboBox.FindStringExact(mapLayer);

                if (checkboxIndex > -1)
                {
                    worldLayersComboBox.SetItemCheckState(checkboxIndex, CheckState.Checked);
                }
            }

            worldLayersComboBox.EndUpdate();

            SetEnabledLayers(loadedMapDocument.DefaultEnabledLayers);
        }

        protected override List<EntityLump.Entity> GetLinkableEntities()
            => loadedMapDocument?.Entities ?? [];
    }
}

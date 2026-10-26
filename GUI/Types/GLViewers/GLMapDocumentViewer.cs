using System.Diagnostics;
using System.Linq;
using GUI.Controls;
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
        // How the exposure is chosen, as Hammer's tonemapping scale: kept at one, taken from the map's post
        // processing volumes, or set by hand
        private enum TonemapMode
        {
            None,
            Auto,
            Override,
        }

        private MapDocumentLoader? loadedMapDocument;
        private TonemapMode tonemapMode = TonemapMode.None;
        private float tonemapScaleOverride = 1f;

        public override void Dispose()
        {
            base.Dispose();

            loadedMapDocument?.Dispose();
        }

        public override void PreSceneLoad()
        {
            base.PreSceneLoad();

            // Lit as Hammer shows a map before its lighting is built: only directly, by its own lights
            LoadUnbakedLighting();
        }

        protected override void LoadScene()
        {
            base.LoadScene();

            loadedMapDocument = new MapDocumentLoader(mapDocument, Scene, EntityVisuals)
            {
                LoadingProgress = GuiContext.LoadingProgress,
                DefaultEnabledLayers = { HammerEntityVisuals.MarkerLayerName },
            };

            loadedMapDocument.Load();

            // No exposure is added on top of what the tonemapping scale gives
            Renderer.Postprocess.ExposureCompensation = 0f;
            ApplyTonemapMode();

            // Opens where the map was last looked at in Hammer
            var camera = mapDocument.Root.DefaultCamera;

            if (camera.Position != camera.LookAt)
            {
                Input.Camera.SetLocation(camera.Position);
                Input.Camera.LookAt(camera.LookAt);
            }
        }

        protected override void AddUiControls()
        {
            base.AddUiControls();

            if (loadedMapDocument == null || worldLayersComboBox == null)
            {
                return;
            }

            SetAvailableLayers(Scene.AllNodes.Select(static r => r.LayerName).OfType<string>(), loadedMapDocument.DefaultEnabledLayers);

            if (!loadedMapDocument.HasLights || !loadedMapDocument.HasBakedLighting)
            {
                SelectRenderMode("FullBright");
            }

            AddTonemapControls();
        }

        private void AddTonemapControls()
        {
            Debug.Assert(UiControl != null);

            using var _ = UiControl.BeginGroup("Tonemapping Scale");

            var modeComboBox = UiControl.AddSelection("Mode", (_, index) =>
            {
                tonemapMode = (TonemapMode)index;
                ApplyTonemapMode();
            });

            modeComboBox.Items.AddRange(Enum.GetNames<TonemapMode>());
            modeComboBox.SelectedIndex = (int)tonemapMode;

            UiControl.AddControl(RendererControl.CreateFloatInput("Override Scale", value =>
            {
                tonemapScaleOverride = value;
                ApplyTonemapMode();
            }, tonemapScaleOverride, minValue: 0.01f, maxValue: 100f));
        }

        private void ApplyTonemapMode()
        {
            Renderer.Postprocess.CustomExposure = tonemapMode switch
            {
                TonemapMode.Auto => -1f,
                TonemapMode.Override => tonemapScaleOverride,
                _ => 1f,
            };
        }

        protected override bool HasToolsVisibilityControls => true;

        // The entities of a map being edited are only drawn, never spawned
        protected override bool CanFireConnections => false;

        protected override List<EntityLump.Entity> GetLinkableEntities()
            => loadedMapDocument?.Entities ?? [];
    }
}

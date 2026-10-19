using System.Collections.Concurrent;
using System.Diagnostics;
using System.Linq;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Editor.Entities;
using ValveResourceFormat.Renderer.Editor.Gizmos;
using ValveResourceFormat.Renderer.Editor.Map;
using ValveResourceFormat.Renderer.Editor.ToolRender;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.World;
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

        private static readonly Color32 GizmoLabelColor = new(220, 220, 220);

        private MapDocumentLoader? loadedMapDocument;
        private TonemapMode tonemapMode = TonemapMode.None;
        private float tonemapScaleOverride = 1f;

        // The gizmo of the selection lives on the render thread, where the UI thread queues what the mouse and keys do
        private GizmoTool? gizmoTool;
        private readonly ConcurrentQueue<Action<GizmoTool>> gizmoCommands = new();
        private long gizmoMouse;

        // Whether the gizmo took the press of the left button, so the release is its too
        private bool gizmoHoldsButton;

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
            Scene.LightingInfo.CubemapType = CubemapType.None;
            Scene.LightingInfo.LightingData.UnbakedIndirect = Vector4.Zero;
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

            gizmoTool = new GizmoTool(Selection, loadedMapDocument.FindTransformable);

            // Hammer shows tools materials until they are hidden
            Tools.ShowToolMaterials = true;

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

        protected override ToolGeometry? GizmoShapes => gizmoTool?.Shapes;

        protected override (string Text, Color32 Color)? ModeLabel
            => base.ModeLabel ?? (gizmoTool is { Shapes.IsEmpty: false } tool ? (tool.StatusText, GizmoLabelColor) : null);

        protected override void UpdateSelectionTools(Camera camera)
        {
            if (gizmoTool == null)
            {
                return;
            }

            while (gizmoCommands.TryDequeue(out var command))
            {
                command(gizmoTool);
            }

            gizmoTool.ToggleSnapping = (Control.ModifierKeys & Keys.Control) != 0;

            var cameraMoving = (CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) != 0;
            var mouse = Interlocked.Read(ref gizmoMouse);

            gizmoTool.Update(camera, new Vector2((int)(mouse >> 32), (int)(mouse & 0xFFFFFFFF)), canHover: MouseOverRenderArea && !cameraMoving && !Input.WalkMode);
        }

        protected override void OnMouseMove(int x, int y)
        {
            Interlocked.Exchange(ref gizmoMouse, ((long)x << 32) | (uint)y);
            base.OnMouseMove(x, y);
        }

        // A press on a part of the gizmo drags it, rather than turning the camera or selecting what is under it
        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            if (e.Button == MouseButtons.Left && gizmoTool?.IsHovered == true && !Input.WalkMode)
            {
                var position = new Vector2(e.X, e.Y);

                gizmoCommands.Enqueue(tool => tool.Press(position));
                gizmoHoldsButton = true;
                GLControl?.Focus();
                return;
            }

            base.OnMouseDown(sender, e);
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            if (gizmoHoldsButton && e.Button == MouseButtons.Left)
            {
                gizmoHoldsButton = false;
                gizmoCommands.Enqueue(static tool => tool.Release());
                return;
            }

            base.OnMouseUp(sender, e);
        }

        // Hammer's keys for the gizmo: Q, T, R and E pick it, Tab turns it with the first selected object or the world,
        // [ and ] change the grid, and Ctrl with them the rotation snap. K and J are held to scale a box about its center
        // or without snapping. Tab only does so while a gizmo is shown, and cycles the performance display otherwise.
        protected override void OnKeyDown(Keys keyData)
        {
            if (gizmoTool != null && HandleGizmoKey(gizmoTool, keyData))
            {
                return;
            }

            base.OnKeyDown(keyData);
        }

        private bool HandleGizmoKey(GizmoTool tool, Keys keyData)
        {
            if (tool.IsDragging)
            {
                if (keyData == Keys.Escape)
                {
                    gizmoCommands.Enqueue(static tool => tool.Abort());
                    return true;
                }
            }
            else if ((CurrentlyPressedKeys & TrackedKeys.MouseLeftOrRight) != 0)
            {
                // Flying with the camera, where Q and E move it
                return false;
            }

            Action<GizmoTool>? command = keyData switch
            {
                Keys.Q when !tool.IsDragging => static tool => tool.Mode = ManipulationMode.Select,
                Keys.T when !tool.IsDragging => static tool => tool.Mode = ManipulationMode.Translate,
                Keys.R when !tool.IsDragging => static tool => tool.Mode = ManipulationMode.Rotate,
                Keys.E when !tool.IsDragging => static tool => tool.Mode = ManipulationMode.Scale,
                Keys.Tab when !tool.IsDragging && tool.IsShown => static tool => tool.ToggleSpace(),
                Keys.OemOpenBrackets => static tool => tool.DecreaseGridSize(),
                Keys.OemCloseBrackets => static tool => tool.IncreaseGridSize(),
                Keys.Control | Keys.OemOpenBrackets => static tool => tool.DecreaseRotationSnapAngle(),
                Keys.Control | Keys.OemCloseBrackets => static tool => tool.IncreaseRotationSnapAngle(),
                Keys.K => static tool => tool.BoxScalesAboutCenter = true,
                Keys.J => static tool => tool.BoxScalesUniformly = true,
                _ => null,
            };

            if (command == null)
            {
                return false;
            }

            gizmoCommands.Enqueue(command);
            return true;
        }

        protected override void OnKeyUp(Keys keyCode)
        {
            if (keyCode == Keys.K)
            {
                gizmoCommands.Enqueue(static tool => tool.BoxScalesAboutCenter = false);
            }
            else if (keyCode == Keys.J)
            {
                gizmoCommands.Enqueue(static tool => tool.BoxScalesUniformly = false);
            }

            base.OnKeyUp(keyCode);
        }

        // The entities of a map being edited are only drawn, never spawned
        protected override bool CanFireConnections => false;

        protected override List<EntityLump.Entity> GetLinkableEntities()
            => loadedMapDocument?.Entities ?? [];
    }
}

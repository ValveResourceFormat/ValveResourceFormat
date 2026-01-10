using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;
using ValveResourceFormat.Renderer;

#nullable disable

namespace GUI.Types.GLViewers
{
    internal abstract class GLSceneViewer : GLViewerControl, IDisposable
    {
        protected SceneRenderer SceneRenderer;

        public Scene Scene { get; }
        public Scene SkyboxScene => SceneRenderer.SkyboxScene;
        public SceneSkybox2D Skybox2D => SceneRenderer.Skybox2D;
        public VrfGuiContext GuiContext;

        private bool ShowBaseGrid;
        private bool ShowLightBackground;
        private bool ShowSolidBackground;
        public bool ShowSkybox { get; set; } = true;
        public bool IsWireframe { get; set; }

        private bool showStaticOctree;
        private bool showDynamicOctree;

        private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
        private int renderModeCurrentIndex;
        private ComboBox renderModeComboBox;
        private InfiniteGrid baseGrid;
        private OctreeDebugRenderer staticOctreeRenderer;
        private OctreeDebugRenderer dynamicOctreeRenderer;
        protected SelectedNodeRenderer SelectedNodeRenderer;

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Frustum cullFrustum) : this(vrfGuiContext, rendererContext)
        {
            SceneRenderer.LockedCullFrustum = cullFrustum;
        }

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext) : base(vrfGuiContext, rendererContext)
        {
            GuiContext = vrfGuiContext;

            SceneRenderer = new SceneRenderer(rendererContext);
            Camera = SceneRenderer.Camera;
            Scene = SceneRenderer.Scene;

#if DEBUG
            ShaderHotReload.ShadersReloaded += OnHotReload;
#endif
        }

        public override void Dispose()
        {
            base.Dispose();

            SceneRenderer?.Dispose();

            if (renderModeComboBox != null)
            {
                renderModeComboBox.Dispose();
                renderModeComboBox = null;
            }

#if DEBUG
            ShaderHotReload.ShadersReloaded -= OnHotReload;
#endif
        }

        protected override void AddUiControls()
        {
            UiControl.AddCheckBox("Lock Cull Frustum", false, (v) =>
            {
                SceneRenderer.LockedCullFrustum = v ? Camera.ViewFrustum.Clone() : null;
            });
            UiControl.AddCheckBox("Show Static Octree", showStaticOctree, (v) =>
            {
                showStaticOctree = v;

                if (showStaticOctree)
                {
                    using var lockedGl = MakeCurrent();

                    staticOctreeRenderer.StaticBuild();
                }
            });
            UiControl.AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);
            UiControl.AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
            {
                Scene.ShowToolsMaterials = v;

                SkyboxScene?.ShowToolsMaterials = v;
            });

            AddWireframeToggleControl();

            base.AddUiControls();
        }

        public virtual void PreSceneLoad()
        {
            SceneRenderer.LoadFallbackTextures();
        }

        public virtual void PostSceneLoad()
        {
            Scene.Initialize();
            if (Scene.PhysicsWorld != null)
            {
                Input.PhysicsWorld = Scene.PhysicsWorld;
            }

            SkyboxScene?.Initialize();

            if (Scene.FogInfo.CubeFogActive)
            {
                SceneRenderer.Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.FogCubeTexture);
                SceneRenderer.Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", Scene.FogInfo.CubemapFog.CubemapFogTexture));
            }

            if (Scene.AllNodes.Any() && this is not GLWorldViewer)
            {
                var first = true;
                var bbox = new AABB();

                foreach (var node in Scene.AllNodes)
                {
                    if (first)
                    {
                        first = false;
                        bbox = node.BoundingBox;
                        continue;
                    }

                    bbox = bbox.Union(node.BoundingBox);
                }

                // If there is no bbox, LookAt will break camera, so +1 to location
                var offset = Math.Max(bbox.Max.X, bbox.Max.Z) + 1f * 1.5f;
                offset = Math.Clamp(offset, 0f, 2000f);
                var location = new Vector3(offset, 0, offset);

                if (this is GLAnimationViewer)
                {
                    location = new(offset);
                }

                Input.Camera.SetLocation(location);
                Input.Camera.LookAt(bbox.Center);
            }

            staticOctreeRenderer = new OctreeDebugRenderer(Scene.StaticOctree, Scene.RendererContext, false);
            dynamicOctreeRenderer = new OctreeDebugRenderer(Scene.DynamicOctree, Scene.RendererContext, true);
        }

        protected abstract void LoadScene();

        protected abstract void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo);

        protected override void OnGLLoad()
        {
            baseGrid = new InfiniteGrid(Scene);
            SelectedNodeRenderer = new(Scene.RendererContext);
            Picker = new(Scene.RendererContext, OnPicked);

            SceneRenderer.ShadowTextureSize = Settings.Config.ShadowResolution;
            SceneRenderer.Initialize();

            SceneRenderer.MainFramebuffer = MainFramebuffer;
            SceneRenderer.Postprocess = postProcessRenderer;

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);

            var timer = Stopwatch.StartNew();
            PreSceneLoad();
            LoadScene();
            timer.Stop();
            Log.Debug(GetType().Name, $"Loading scene time: {timer.Elapsed}, shader variants: {Scene.RendererContext.ShaderLoader.ShaderCount}, materials: {Scene.RendererContext.MaterialLoader.MaterialCount}");

            PostSceneLoad();

            GuiContext.ClearCache();
            GuiContext.GLPostLoadAction?.Invoke(this);
            GuiContext.GLPostLoadAction = null;
        }

        protected override void OnUpdate(float frameTime)
        {
            base.OnUpdate(frameTime);

            if (MouseOverRenderArea || Input.ForceUpdate)
            {
                var pressedKeys = CurrentlyPressedKeys;
                var modifierKeys = Control.ModifierKeys;

                if ((modifierKeys & Keys.Shift) > 0)
                {
                    pressedKeys |= TrackedKeys.Shift;
                }

                if ((modifierKeys & Keys.Alt) > 0)
                {
                    pressedKeys |= TrackedKeys.Alt;
                }

                Input.Tick(frameTime, pressedKeys, new Vector2(MouseDelta.X, MouseDelta.Y), Camera);
                LastMouseDelta = MouseDelta;
                MouseDelta = System.Drawing.Point.Empty;
            }
        }

        protected override void OnPaint(float frameTime)
        {
            var renderContext = new Scene.RenderContext
            {
                Camera = Camera,
                Framebuffer = MainFramebuffer,
                Textures = SceneRenderer.Textures,
                Scene = Scene,
            };

            using (new GLDebugGroup("Update Loop"))
            {
                var updateContext = new Scene.UpdateContext
                {
                    TextRenderer = TextRenderer,
                    Timestep = frameTime,
                    Camera = Camera,
                };

                SceneRenderer.Update(updateContext);

                SelectedNodeRenderer.Update(renderContext, updateContext);
            }

            using (new GLDebugGroup("Scenes Render"))
            {
                if (Picker.ActiveNextFrame)
                {
                    using var _ = new GLDebugGroup("Picker Object Id Render");
                    renderContext.ReplacementShader = Picker.Shader;
                    renderContext.Framebuffer = Picker;

                    SceneRenderer.RenderScenesWithView(renderContext);
                    Picker.Finish();
                }
                else if (Picker.IsDebugActive)
                {
                    renderContext.ReplacementShader = Picker.DebugShader;
                }

                SceneRenderer.Render(renderContext);
            }

            using (new GLDebugGroup("Lines Render"))
            {
                SelectedNodeRenderer.Render();

                if (showStaticOctree)
                {
                    staticOctreeRenderer.Render();
                }

                if (showDynamicOctree)
                {
                    dynamicOctreeRenderer.Render();
                }

                if (ShowBaseGrid)
                {
                    baseGrid.Render();
                }
            }

            if (Paused)
            {
                DrawLowerCornerText("Paused", new(255, 100, 0));
            }
            else if (Settings.Config.DisplayFps != 0)
            {
                DrawLowerCornerText(FpsText, Color32.White);
            }
        }

        protected void AddBaseGridControl()
        {
            UiControl.AddDivider();
            var lightBackgroundCheckbox = UiControl.AddCheckBox("Light Background", ShowLightBackground, (v) =>
            {
                ShowLightBackground = v;
                SceneRenderer.BaseBackground.SetLightBackground(ShowLightBackground);
            });

            lightBackgroundCheckbox.Checked = Themer.CurrentTheme == Themer.AppTheme.Light;

            UiControl.AddCheckBox("Solid Background", ShowSolidBackground, (v) =>
            {
                ShowSolidBackground = v;
                SceneRenderer.BaseBackground.SetSolidBackground(ShowSolidBackground);
            });
            UiControl.AddDivider();

            if (this is not GLMaterialViewer)
            {
                ShowBaseGrid = true;
                UiControl.AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);
            }
        }

        protected void AddWireframeToggleControl()
        {
            if (this is GLMaterialViewer)
            {
                return;
            }

            UiControl.AddCheckBox("Show Wireframe", false, (v) => IsWireframe = v);
        }

        protected void AddRenderModeSelectionControl()
        {
            if (renderModeComboBox != null)
            {
                return;
            }

            renderModeComboBox = UiControl.AddSelection("Render Mode", (_, i) =>
            {
                if (renderModeCurrentIndex < -1)
                {
                    renderModeCurrentIndex = i;
                    return;
                }

                if (i < 0)
                {
                    return;
                }

                var renderMode = renderModes[i];

                if (renderMode.IsHeader)
                {
                    renderModeComboBox.SelectedIndex = renderModeCurrentIndex > i ? i - 1 : i + 1;
                    return;
                }

                renderModeCurrentIndex = i;
                SetRenderMode(renderMode.Name);
            }, true, true);

            SetAvailableRenderModes();
        }

        private void SetAvailableRenderModes(bool keepCurrentSelection = false)
        {
            if (renderModeComboBox != null)
            {
                var selectedIndex = 0;
                var currentlySelected = keepCurrentSelection ? renderModeComboBox.SelectedItem.ToString() : null;
                var supportedRenderModes = new HashSet<string>(Picker.Shader.RenderModes);
                foreach (var node in Scene.AllNodes)
                {
                    supportedRenderModes.UnionWith(node.GetSupportedRenderModes());
                }

                renderModes.Clear();

                for (var i = 0; i < RenderModes.Items.Count; i++)
                {
                    var mode = RenderModes.Items[i];

                    if (i > 0)
                    {
                        if (mode.IsHeader)
                        {
                            if (renderModes[^1].IsHeader)
                            {
                                // If we hit a header and the last added item is also a header, remove it
                                renderModes.RemoveAt(renderModes.Count - 1);
                            }
                        }
                        else if (!supportedRenderModes.Remove(mode.Name))
                        {
                            continue;
                        }
                    }

                    if (mode.Name == currentlySelected)
                    {
                        selectedIndex = renderModes.Count;
                    }

                    renderModes.Add(mode);
                }

                renderModeComboBox.BeginUpdate();
                renderModeComboBox.Items.Clear();

                foreach (var renderMode in renderModes)
                {
                    renderModeComboBox.Items.Add(new ThemedComboBoxItem { Text = renderMode.Name, IsHeader = renderMode.IsHeader });
                }

                renderModeCurrentIndex = -10;
                renderModeComboBox.SelectedIndex = selectedIndex;
                renderModeComboBox.EndUpdate();
            }
        }

        protected void SetEnabledLayers(HashSet<string> layers)
        {
            Scene.SetEnabledLayers(layers);
            SkyboxScene?.SetEnabledLayers(layers);

            if (showStaticOctree)
            {
                using var lockedGl = MakeCurrent();

                staticOctreeRenderer.Rebuild();
            }
        }

        private void SetRenderMode(string renderMode)
        {
            SceneRenderer.ViewBuffer.Data.RenderMode = RenderModes.GetShaderId(renderMode);

            SceneRenderer.Postprocess.Enabled = SceneRenderer.ViewBuffer.Data.RenderMode == 0;

            Picker.SetRenderMode(renderMode);
            SelectedNodeRenderer.SetRenderMode(renderMode);

            foreach (var node in Scene.AllNodes)
            {
                node.SetRenderMode(renderMode);
            }

            if (SkyboxScene != null)
            {
                foreach (var node in SkyboxScene.AllNodes)
                {
                    node.SetRenderMode(renderMode);
                }
            }
        }

        protected override void OnKeyDown(object sender, KeyEventArgs e)
        {
            if (e.KeyData == Keys.Delete)
            {
                SelectedNodeRenderer.DisableSelectedNodes();
                return;
            }

            if (e.KeyData == Keys.Escape)
            {
                SelectedNodeRenderer.SelectNode(null);
            }

            base.OnKeyDown(sender, e);
        }

#if DEBUG
        private void OnHotReload(object sender, string e)
        {
            using var lockedGl = MakeCurrent();

            if (renderModeComboBox != null)
            {
                SetAvailableRenderModes(true);
            }

            foreach (var node in Scene.AllNodes)
            {
                node.UpdateVertexArrayObjects();
            }

            if (SkyboxScene != null)
            {
                foreach (var node in SkyboxScene.AllNodes)
                {
                    node.UpdateVertexArrayObjects();
                }
            }

            GLControl.Invalidate();
        }
#endif
    }
}

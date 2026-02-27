using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Renderer;
using static ValveResourceFormat.Renderer.PickingTexture;

namespace GUI.Types.GLViewers
{
    internal abstract class GLSceneViewer : GLBaseControl
    {
        public Renderer Renderer { get; internal set; }
        public UserInput Input { get; protected set; }

        public ValveResourceFormat.Renderer.TextRenderer TextRenderer { get; protected set; }

        protected PickingTexture? Picker { get; set; }

        public Scene Scene { get; }
        public Scene? SkyboxScene => Renderer.SkyboxScene;
        public VrfGuiContext GuiContext;

        private bool ShowBaseGrid;
        private bool ShowLightBackground;
        private bool ShowSolidBackground;

        private bool showStaticOctree;
        private bool showDynamicOctree;

        private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
        private int renderModeCurrentIndex;
        private ComboBox? renderModeComboBox;
        private InfiniteGrid? baseGrid;
        protected SelectedNodeRenderer? SelectedNodeRenderer;

        static readonly TimeSpan FpsUpdateTimeSpan = TimeSpan.FromSeconds(0.1);

        private readonly float[] frameTimes = new float[30];
        private int frameTimeNextId;
        private string fpsText = string.Empty;
        private int frametimeQuery1;
        private int frametimeQuery2;

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext, Frustum cullFrustum) : this(vrfGuiContext, rendererContext)
        {
            Renderer.LockedCullFrustum = cullFrustum;
        }

        protected GLSceneViewer(VrfGuiContext vrfGuiContext, RendererContext rendererContext) : base(rendererContext)
        {
            GuiContext = vrfGuiContext;

            Renderer = new(rendererContext);
            Input = new UserInput(Renderer);
            TextRenderer = new(rendererContext, Renderer.Camera);
            Scene = Renderer.Scene;

#if DEBUG
            ShaderHotReload.ShadersReloaded += OnHotReload;
#endif
        }

        public override void Dispose()
        {
            base.Dispose();

            Renderer?.Dispose();

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
            Debug.Assert(UiControl != null);

            using (UiControl.BeginGroup("Debug"))
            {
                UiControl.AddCheckBox("Lock Cull Frustum", false, (v) =>
                {
                    Renderer.LockedCullFrustum = v ? Renderer.Camera.ViewFrustum.Clone() : null;
                });

                UiControl.AddCheckBox("Show Static Octree", showStaticOctree, (v) => showStaticOctree = v);
                UiControl.AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);
                UiControl.AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
                {
                    Scene.ShowToolsMaterials = v;

                    SkyboxScene?.ShowToolsMaterials = v;
                });

                if (this is GLWorldViewer worldViewer)
                {
                    UiControl.AddCheckBox("Show Occluded Bounds", Scene.OcclusionDebugEnabled, (v) => Scene.OcclusionDebugEnabled = v);
                }

                UiControl.AddCheckBox("Show Render Timings", Renderer.Timings.Capture, (v) => Renderer.Timings.Capture = v);
            }

            base.AddUiControls();
        }

        public virtual void PreSceneLoad()
        {
            Renderer.LoadRendererResources();
        }

        // Default environment + simple sun lighting used by viewers without lighting information
        protected readonly Vector2 defaultSunAngles = new(80f, 170f);
        protected readonly Vector4 defaultSunColor = new(new Vector3(255, 247, 235) / 255.0f, 2.5f);
        protected Vector2 sunAngles;
        private bool loadedDefaultLighting;

        protected virtual void LoadDefaultLighting()
        {
            using var stream = Program.Assembly.GetManifestResourceStream("GUI.Utils.industrial_sunset_puresky.vtex_c");
            Debug.Assert(stream != null);

            using var resource = new ValveResourceFormat.Resource()
            {
                FileName = "vrf_default_cubemap.vtex_c"
            };
            resource.Read(stream);

            var texture = Scene.RendererContext.MaterialLoader.LoadTexture(resource, true);
            var environmentMap = new SceneEnvMap(Scene, new AABB(new Vector3(float.MinValue), new Vector3(float.MaxValue)))
            {
                Transform = Matrix4x4.Identity,
                EdgeFadeDists = Vector3.Zero,
                HandShake = 0,
                ProjectionMode = 0,
                EnvMapTexture = texture,
            };

            Scene.LightingInfo.AddEnvironmentMap(environmentMap);
            Scene.LightingInfo.UseSceneBoundsForSunLightFrustum = true;

            sunAngles = defaultSunAngles;
            Scene.LightingInfo.LightingData.LightColor_Brightness[0] = defaultSunColor;
            UpdateSunAngles();
            loadedDefaultLighting = true;
        }

        protected void UpdateSunAngles()
        {
            // clamp and wrap angles
            sunAngles.X = Math.Clamp(sunAngles.X, 0f, 89f);
            sunAngles.Y %= 360f;

            Scene.LightingInfo.LightingData.LightToWorld[0] = Matrix4x4.CreateRotationY(float.DegreesToRadians(sunAngles.X))
                                                             * Matrix4x4.CreateRotationZ(float.DegreesToRadians(sunAngles.Y));
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
                var cubemapTexture = Scene.FogInfo.CubemapFog?.CubemapFogTexture;
                if (cubemapTexture != null)
                {
                    Renderer.Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.FogCubeTexture);
                    Renderer.Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", cubemapTexture));
                }
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

            Scene.StaticOctree.DebugRenderer = new(Scene.StaticOctree, Scene.RendererContext, false);
            Scene.DynamicOctree.DebugRenderer = new(Scene.DynamicOctree, Scene.RendererContext, true);
        }

        protected abstract void LoadScene();

        protected abstract void OnPicked(object? sender, PickingTexture.PickingResponse pixelInfo);

        protected override void OnResize(int w, int h)
        {
            base.OnResize(w, h);

            Renderer.Camera.SetViewportSize(w, h);
            Picker?.Resize(w, h);
        }

        protected override void OnMouseWheel(object? sender, MouseEventArgs e)
        {
            base.OnMouseWheel(sender, e);

            if (!Input.NoClip)
            {
                return;
            }

            var modifier = Input.OnMouseWheel(e.Delta);

            if (Input.OrbitMode)
            {
                SetMoveSpeedOrZoomLabel($"Orbit distance: {modifier:0.0} (scroll to change)");
            }
            else
            {
                SetMoveSpeedOrZoomLabel($"Move speed: {modifier:0.0}x (scroll to change)");
            }
        }

        protected override void OnMouseUp(object? sender, MouseEventArgs e)
        {
            base.OnMouseUp(sender, e);

            if (!Input.NoClip)
            {
                return;
            }

            if (InitialMousePosition == new Point(e.X, e.Y))
            {
                Picker?.RequestNextFrame(e.X, e.Y, PickingIntent.Select);
            }
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            base.OnMouseDown(sender, e);

            if (!Input.NoClip)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (e.Clicks == 2)
                {
                    var intent = Control.ModifierKeys.HasFlag(Keys.Control)
                        ? PickingIntent.Open
                        : PickingIntent.Details;
                    Picker?.RequestNextFrame(e.X, e.Y, intent);
                }
            }
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out frametimeQuery1);
            GL.CreateQueries(QueryTarget.TimeElapsed, 1, out frametimeQuery2);

#if DEBUG
            const string queryLabel = "Frame Time Query";
            GL.ObjectLabel(ObjectLabelIdentifier.Query, frametimeQuery1, queryLabel.Length, queryLabel);
            GL.ObjectLabel(ObjectLabelIdentifier.Query, frametimeQuery2, queryLabel.Length, queryLabel);
#endif

            // Needed to fix crash on certain drivers
            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery2);
            GL.EndQuery(QueryTarget.TimeElapsed);

            TextRenderer.Load();
            Renderer.Postprocess.Load();

            baseGrid = new InfiniteGrid(Scene);
            SelectedNodeRenderer = new(Scene.RendererContext);
            Picker = new(Scene.RendererContext, OnPicked);

            Renderer.ShadowTextureSize = Settings.Config.ShadowResolution;
            Renderer.Initialize();

            Renderer.MainFramebuffer = MainFramebuffer;

            MainFramebuffer!.Bind(FramebufferTarget.Framebuffer);

            var timer = Stopwatch.StartNew();
            PreSceneLoad();
            LoadScene();
            timer.Stop();
            Log.Debug(GetType().Name, $"Loading scene time: {timer.Elapsed}, shader variants: {Scene.RendererContext.ShaderLoader.ShaderCount}, materials: {Scene.RendererContext.MaterialLoader.MaterialCount}");

            PostSceneLoad();

            if (GLNativeWindow != null)
            {
                // try to compile shaders?
                Renderer.Camera.SetLocationPitchYaw(Vector3.UnitZ * 20_000f, -90, 0f);
                Renderer.Camera.SetViewportSize(64, 64);
                OnPaint(0f);
                GLNativeWindow.Context.SwapBuffers();
            }

            GuiContext.ClearCache();
            GuiContext.GLPostLoadAction?.Invoke(this);
            GuiContext.GLPostLoadAction = null;
        }

        protected override void OnUpdate(float frameTime)
        {
            base.OnUpdate(frameTime);

            Input.EnableMouseLook = true;
            if (loadedDefaultLighting && (CurrentlyPressedKeys & TrackedKeys.Control) != 0)
            {
                var delta = new Vector2(LastMouseDelta.Y, LastMouseDelta.X);

                sunAngles += delta;
                Scene.AdjustEnvMapSunAngle(Matrix4x4.CreateRotationZ(-delta.Y / 80f));
                UpdateSunAngles();
                Scene.UpdateBuffers();
                Input.EnableMouseLook = false;
            }

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

                Input.Tick(frameTime, pressedKeys, new Vector2(MouseDelta.X, MouseDelta.Y), Renderer.Camera);
                LastMouseDelta = MouseDelta;
                MouseDelta = System.Drawing.Point.Empty;

                // Clear mouse wheel events after processing (they're one-time events)
                CurrentlyPressedKeys &= ~(TrackedKeys.MouseWheelUp | TrackedKeys.MouseWheelDown);

                GrabbedMouse = !Input.NoClip && !Paused;
            }
        }

        protected void DrawLowerCornerText(string text, Color32 color)
        {
            Debug.Assert(MainFramebuffer != null);

            TextRenderer.AddText(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
            {
                X = 2f,
                Y = MainFramebuffer.Height - 4f,
                Scale = 14f,
                Color = color,
                Text = text
            });
        }

        protected override void BlitFramebufferToScreen()
        {
            if (MainFramebuffer == GLDefaultFramebuffer)
            {
                return; // not required
            }

            Debug.Assert(MainFramebuffer != null);
            Debug.Assert(GLDefaultFramebuffer != null);

            Renderer.PostprocessRender(MainFramebuffer, GLDefaultFramebuffer);
        }

        protected override void OnPaint(float frameTime)
        {
            Debug.Assert(MainFramebuffer != null);
            Debug.Assert(Picker != null);
            Debug.Assert(SelectedNodeRenderer != null);

            Renderer.Timings.MarkFrameBegin();
            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery1);

            var renderContext = new Scene.RenderContext
            {
                Camera = Renderer.Camera,
                Framebuffer = MainFramebuffer,
                Textures = Renderer.Textures,
                Scene = Scene,
            };

            using (new GLDebugGroup("Update Loop"))
            {
                var updateContext = new Scene.UpdateContext
                {
                    TextRenderer = TextRenderer,
                    Timestep = frameTime,
                    Camera = Renderer.Camera,
                };

                Renderer.Update(updateContext);

                SelectedNodeRenderer.Update(renderContext, updateContext);
            }

            using (new GLDebugGroup("Scenes Render"))
            {
                if (Picker.ActiveNextFrame)
                {
                    using var _ = new GLDebugGroup("Picker Object Id Render");
                    renderContext.ReplacementShader = Picker.Shader;
                    renderContext.Framebuffer = Picker;

                    Renderer.RenderScenesWithView(renderContext);
                    Picker.Finish();
                }
                else if (Picker.IsDebugActive)
                {
                    renderContext.ReplacementShader = Picker.DebugShader;
                }

                Renderer.Render(renderContext);
            }

            using (new GLDebugGroup("Lines Render"))
            {
                SelectedNodeRenderer.Render();

                if (showStaticOctree && Scene.StaticOctree.DebugRenderer != null)
                {
                    Scene.StaticOctree.DebugRenderer.Render();
                }

                if (showDynamicOctree && Scene.DynamicOctree.DebugRenderer != null)
                {
                    Scene.DynamicOctree.DebugRenderer.Render();
                }

                if (Scene.OcclusionDebugEnabled && Scene.OcclusionDebug != null)
                {
                    Scene.OcclusionDebug.Render();
                }

                if (ShowBaseGrid && baseGrid != null)
                {
                    baseGrid.Render();
                }
            }

            GL.EndQuery(QueryTarget.TimeElapsed);

            if (Paused)
            {
                DrawLowerCornerText("Paused", new(255, 100, 0));
            }
            else if (Settings.Config.DisplayFps != 0)
            {
                var currentTime = Stopwatch.GetTimestamp();
                var fpsElapsed = Stopwatch.GetElapsedTime(lastFpsUpdate, currentTime);

                frameTimes[frameTimeNextId++] = frameTime;
                frameTimeNextId %= frameTimes.Length;

                if (fpsElapsed >= FpsUpdateTimeSpan)
                {
                    var frametimeQuery = frametimeQuery2;
                    frametimeQuery2 = frametimeQuery1;
                    frametimeQuery1 = frametimeQuery;

                    GL.GetQueryObject(frametimeQuery, GetQueryObjectParam.QueryResultNoWait, out long gpuTime);
                    var gpuFrameTime = gpuTime / 1_000_000f;

                    var fps = 1f / (frameTimes.Sum() / frameTimes.Length);
                    var cpuFrameTime = Stopwatch.GetElapsedTime(LastUpdate, currentTime).TotalMilliseconds;

                    lastFpsUpdate = currentTime;
                    fpsText = $"FPS: {fps,-3:0}  CPU: {cpuFrameTime,-4:0.0}ms  GPU: {gpuFrameTime,-4:0.0}ms";
                }

                DrawLowerCornerText(fpsText, Color32.White);
            }

            BlitFramebufferToScreen();

            if (GrabbedMouse)
            {
                TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 0.5f,
                    Y = 0.02f,
                    Scale = 14f,
                    Color = new Color32(0, 150, 255),
                    Text = "* MOVEMENT IS EXPERIMENTAL. EXPECT BUGS. HELP US IMPROVE IT. *",
                    CenterVertical = true,
                }, Renderer.Camera);

                TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 0.5f,
                    Y = 0.85f,
                    Scale = 12f,
                    Color = Color32.Yellow,
                    Text = $"Speed: {Input.Velocity.AsVector2().Length():0.0} u/s",
                    CenterVertical = true,
                }, Renderer.Camera);
            }

            if (Renderer.Timings.Capture)
            {
                Renderer.Timings.DisplayTimings(TextRenderer, Renderer.Camera);
            }

            TextRenderer.Render(Renderer.Camera);
            Picker?.TriggerEventIfAny();

            Renderer.Timings.MarkFrameEnd();
        }

        protected void AddBaseGridControl()
        {
            Debug.Assert(UiControl != null);

            using var _ = UiControl.BeginGroup("Display");

            var lightBackgroundCheckbox = UiControl.AddCheckBox("Light Background", ShowLightBackground, (v) =>
            {
                ShowLightBackground = v;
                Renderer.BaseBackground!.SetLightBackground(ShowLightBackground);
            });

            lightBackgroundCheckbox.Checked = Themer.CurrentTheme == Themer.AppTheme.Light;

            UiControl.AddCheckBox("Solid Background", ShowSolidBackground, (v) =>
            {
                ShowSolidBackground = v;
                Renderer.BaseBackground!.SetSolidBackground(ShowSolidBackground);
            });

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

            Debug.Assert(UiControl != null);

            UiControl.AddCheckBox("Show Wireframe", Renderer.IsWireframe, (v) => Renderer.IsWireframe = v);
        }

        protected void AddRenderModeSelectionControl()
        {
            if (renderModeComboBox != null)
            {
                return;
            }

            Debug.Assert(UiControl != null);

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
                    renderModeComboBox!.SelectedIndex = renderModeCurrentIndex > i ? i - 1 : i + 1;
                    return;
                }

                renderModeCurrentIndex = i;
                SetRenderMode(renderMode.Name);
            }, true, true);

            SetAvailableRenderModes();
        }

        private void SetAvailableRenderModes(bool keepCurrentSelection = false)
        {
            if (renderModeComboBox != null && Picker != null)
            {
                var selectedIndex = 0;
                var currentlySelected = keepCurrentSelection ? renderModeComboBox.SelectedItem?.ToString() : null;
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
            using var glLock = MakeCurrent();
            Scene.SetEnabledLayers(layers);
            SkyboxScene?.SetEnabledLayers(layers);
        }

        private void SetRenderMode(string renderMode)
        {
            Debug.Assert(Picker != null);
            Debug.Assert(SelectedNodeRenderer != null);

            Renderer.ViewBuffer!.Data!.RenderMode = RenderModes.GetShaderId(renderMode);

            Renderer.Postprocess.Enabled = Renderer.ViewBuffer.Data.RenderMode == 0;

            Scene.EnableCompaction = renderMode != "Meshlets";

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

        protected override void OnKeyDown(object? sender, KeyEventArgs e)
        {
            Debug.Assert(SelectedNodeRenderer != null);

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
        private void OnHotReload(object? sender, string? e)
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

            GLControl?.Invalidate();
        }
#endif
    }
}

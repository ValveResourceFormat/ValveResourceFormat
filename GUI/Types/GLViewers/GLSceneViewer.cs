using System.Diagnostics;
using System.Drawing;
using System.Linq;
using System.Runtime.InteropServices;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Audio;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat.Editor;
using ValveResourceFormat.Editor.Entities;
using ValveResourceFormat.Editor.Picking;
using ValveResourceFormat.Editor.Selection;
using ValveResourceFormat.Renderer;
using ValveResourceFormat.Renderer.Audio;
using ValveResourceFormat.Renderer.Input;
using ValveResourceFormat.Renderer.Materials;
using ValveResourceFormat.Renderer.SceneEnvironment;
using ValveResourceFormat.Renderer.SceneNodes;

namespace GUI.Types.GLViewers
{
    internal abstract class GLSceneViewer : GLBaseControl
    {
        public ValveResourceFormat.Renderer.Renderer Renderer { get; internal set; }
        public UserInput Input { get; protected set; }

        /// <summary>Whether the world is being viewed or run as in game, which picking and the mode controls follow.</summary>
        protected EditorState EditorState { get; }

        /// <summary>Gets whether the viewport shows the editor mode, for viewers that can run the world as in game.</summary>
        protected virtual bool ShowsEditorMode => false;

        public ValveResourceFormat.Renderer.TextRenderer TextRenderer { get; protected set; }
        private readonly CrosshairRenderer crosshairRenderer;

        protected ScenePicker? Picker { get; private set; }

        /// <summary>The nodes selected in this viewer, drawn with an outline and helpers.</summary>
        protected SelectionSet Selection { get; } = new();

        private volatile HashSet<string>? chosenLayers;
        private volatile bool visibilityDirty;

        /// <summary>Text drawn in the top-left corner of the viewport, such as stats for the selection.</summary>
        protected string ScreenDebugText { get; set; } = string.Empty;

        protected QuadOverdraw? QuadOverdrawRenderer { get; set; }

        public Scene Scene { get; }
        public Scene? SkyboxScene => Renderer.SkyboxScene;
        public VrfGuiContext GuiContext;

        /// <summary>Optional sound event player, created by viewers that play scene audio.</summary>
        protected SoundEventPlayer? soundPlayer;

        /// <summary>Gets whether this viewer plays scene audio, i.e. whether <see cref="InitializeSoundPlayer"/> got a device.</summary>
        public bool HasSoundPlayer => soundPlayer != null;

        /// <summary>Gets or sets whether this viewer's audio is silenced, independently of the master volume.</summary>
        public bool Muted
        {
            get => soundPlayer?.Mute ?? false;
            set
            {
                if (soundPlayer != null)
                {
                    soundPlayer.Mute = value;
                }
            }
        }

        private bool ShowBaseGrid;
        private bool ShowLightBackground;
        private bool ShowSolidBackground;

        private bool showStaticOctree;
        private bool showDynamicOctree;
        private bool showVisDebug;
        protected bool ShowSpeed { get; set; }
        private bool showPhysicsTraces;
        private PhysicsTraceDebugRenderer? physicsTraceRenderer;

        private enum PerfDisplay
        {
            Off,
            Stats,
            Timings,
            Allocations,
        }

        private PerfDisplay perfDisplay;
        private ComboBox? perfDisplayComboBox;

        private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
        private int renderModeCurrentIndex;
        private ComboBox? renderModeComboBox;
        private ComboBox? cubemapColorsComboBox;
        private bool isCubemapsRenderMode;
        private InfiniteGrid? baseGrid;
        private SelectionVisuals? selectionVisuals;

        static readonly TimeSpan FpsUpdateTimeSpan = TimeSpan.FromSeconds(0.1);

        private readonly float[] frameTimes = new float[30];
        private int frameTimeNextId;
        private int frameTimeCount;

        private readonly ValveResourceFormat.Renderer.TextRenderer.TextBuffer fpsText = new("FPS: 10000  CPU: 10000.0ms  GPU: 10000.0ms");
        private readonly ValveResourceFormat.Renderer.TextRenderer.TextBuffer speedText = new("Speed: 100000.0 u/s");
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
            Renderer.EntitySystem.ToolVisuals = new HammerEntityVisuals();
            EditorState = new EditorState(Renderer.EntitySystem, Input);
            EditorState.ModeChanged += OnEditorModeChanged;
            EditorState.WalkingChanged += OnWalkingChanged;
            TextRenderer = new(rendererContext, Renderer.Camera);
            crosshairRenderer = new CrosshairRenderer(rendererContext);
            Scene = Renderer.Scene;

#if DEBUG
            ShaderHotReload.ShadersReloaded += OnHotReload;
#endif
        }

        public override void Dispose()
        {
            // Delete GL resources before the base disposes the GL context
            physicsTraceRenderer?.Delete();
            physicsTraceRenderer = null;

            selectionVisuals?.Dispose();
            selectionVisuals = null;

            Picker?.Dispose();
            Picker = null;

            soundPlayer?.Dispose();
            soundPlayer = null;

            QuadOverdrawRenderer?.Dispose();
            QuadOverdrawRenderer = null;

            Renderer?.Dispose();

            base.Dispose();

            perfDisplayComboBox?.Dispose();
            perfDisplayComboBox = null;

            renderModeComboBox?.Dispose();
            renderModeComboBox = null;

            cubemapColorsComboBox?.Dispose();
            cubemapColorsComboBox = null;

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
                    Renderer.LockedCullPosition = v ? Renderer.Camera.Location : null;
                });

                UiControl.AddCheckBox("Show Static Octree", showStaticOctree, (v) => showStaticOctree = v);
                UiControl.AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);

                // Viewers with an editor mode have their own tools visibility controls
                if (!ShowsEditorMode)
                {
                    UiControl.AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
                    {
                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.ShowToolsMaterials = v;
                        }
                    });
                }

                if (Renderer.Scenes.Any(static scene => scene.LightingInfo.LightProbes.Count > 0))
                {
                    var lightProbeGridComboBox = UiControl.AddSelection("Light Probe Grid", (_, i) =>
                    {
                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.LightingDebug.LightProbeGrid = (LightProbeDebugGridMode)i;
                        }
                    });

                    lightProbeGridComboBox.Items.AddRange(["Off", "Closest", "Closest and keep", "All"]);
                    lightProbeGridComboBox.SelectedIndex = (int)Scene.LightingDebug.LightProbeGrid;

                    // Mirror samples show the cubemaps reaching into each volume
                    var lightProbeGridSurfaceComboBox = UiControl.AddSelection("Light Probe Grid Surface", (_, i) =>
                    {
                        var mirror = i == 1;

                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.LightingDebug.LightProbeGridAlbedo = mirror ? new Color32(255, 255, 255, 255) : new Color32(128, 128, 128, 255);
                            scene.LightingDebug.LightProbeGridRoughness = mirror ? 0f : 0.5f;
                            scene.LightingDebug.LightProbeGridMetalness = mirror ? 1f : 0f;
                        }
                    });

                    lightProbeGridSurfaceComboBox.Items.AddRange(["Diffuse", "Mirror"]);
                    lightProbeGridSurfaceComboBox.SelectedIndex = 0;

                    UiControl.AddCheckBox("Light Probe Grid Cubes", Scene.LightingDebug.LightProbeGridCubes, v =>
                    {
                        foreach (var scene in Renderer.Scenes)
                        {
                            scene.LightingDebug.LightProbeGridCubes = v;
                        }
                    });
                }

                if (Renderer.Scenes.Any(static scene => scene.LightingInfo.EnvMaps.Exists(static envMap => envMap.EntityData != null)))
                {
                    cubemapColorsComboBox = UiControl.AddSelection("Cubemap Debug Colors", (_, _) => ApplyCubemapColors());
                    cubemapColorsComboBox.Items.AddRange(["Off", "Reflections and markers", "Markers only"]);
                    cubemapColorsComboBox.SelectedIndex = 0;
                    cubemapColorsComboBox.Parent!.Visible = isCubemapsRenderMode;
                }

                if (this is GLWorldViewer)
                {
                    UiControl.AddCheckBox("Show Occluded Bounds", Scene.OcclusionDebugEnabled, (v) => Scene.OcclusionDebugEnabled = v);

                    if (Scene.VoxelVisibility != null)
                    {
                        UiControl.AddCheckBox("Show Vis Debug", showVisDebug, v => showVisDebug = v);
                    }
                }

                if (Renderer.EntitySystem.PhysicsWorld != null)
                {
                    UiControl.AddCheckBox("Debug Physics Traces", showPhysicsTraces, v => showPhysicsTraces = v);
                }

                UiControl.AddCheckBox("Debug Sound Sources", Renderer.ShowSoundDebug, v => Renderer.ShowSoundDebug = v);

                UiControl.AddCheckBox("Disable threaded sim", !Renderer.ParallelSimulation, v => Renderer.ParallelSimulation = !v);

                perfDisplayComboBox = UiControl.AddSelection("Debug Performance", (_, i) => perfDisplay = (PerfDisplay)i);
                perfDisplayComboBox.Items.AddRange([nameof(PerfDisplay.Off), nameof(PerfDisplay.Stats), nameof(PerfDisplay.Timings), nameof(PerfDisplay.Allocations)]);
                perfDisplayComboBox.SelectedIndex = (int)perfDisplay;
            }

            base.AddUiControls();
        }

        public virtual void PreSceneLoad()
        {
            Renderer.LoadRendererResources();
        }

        public Vector2 sunAngles;
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

            Renderer.LoadDefaultLighting(Scene, resource);

            sunAngles = Renderer.DefaultSunAngles;
            loadedDefaultLighting = true;
        }

        protected void UpdateSunAngles()
        {
            sunAngles.X = Math.Clamp(sunAngles.X, 0f, 89f);
            sunAngles.Y = MathUtils.Wrap(sunAngles.Y, 0f, 360f);

            Scene.LightingInfo.SetSunDirectionFromAngles(new Vector3(sunAngles.X, sunAngles.Y, 0f));
        }

        public virtual void PostSceneLoad()
        {
            foreach (var scene in Renderer.Scenes)
            {
                scene.Initialize();
            }

            Input.PhysicsWorld = Renderer.EntitySystem.PhysicsWorld;

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
                var offset = bbox.Max.MaxComponent() + 1f * 1.5f;
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
            Scene.DynamicOctree.DebugRenderer = new(Scene.DynamicOctree, Scene.RendererContext);
        }

        protected abstract void LoadScene();

        /// <summary>Follows a change of editor mode, on the render thread. Entering game mode drops the selection.</summary>
        /// <param name="mode">The new mode.</param>
        protected virtual void OnEditorModeChanged(EditorMode mode)
        {
            // The tools visibility is kept per mode
            RequestVisibilityUpdate();

            if (mode == EditorMode.Game)
            {
                Selection.Clear();
            }
        }

        /// <summary>Follows the camera starting or stopping walking as the player, on the render thread.</summary>
        /// <param name="walking">Whether the camera now walks.</param>
        protected virtual void OnWalkingChanged(bool walking)
        {
        }

        /// <summary>Handles a pick resolved on the render thread. Viewers that do nothing with picks leave this empty.</summary>
        protected virtual void OnPicked(PickResult result)
        {
        }

        protected override void OnResize(int w, int h)
        {
            base.OnResize(w, h);

            Renderer.Camera.SetViewportSize(w, h);

            // The input camera frames objects against its own aspect ratio, so it needs the size too
            Input.Camera.SetViewportSize(w, h);

            Picker?.Resize(w, h);
        }

        protected override void OnMouseWheel(int delta, Point location)
        {
            base.OnMouseWheel(delta, location);

            if (Input.WalkMode)
            {
                return;
            }

            var modifier = Input.OnMouseWheel(delta);

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

            if (EditorState.Mode != EditorMode.Viewer)
            {
                return;
            }

            // Only a left click picks, the right button is for moving the camera
            if (e.Button == MouseButtons.Left && (!MouseDragged || GrabbedMouse))
            {
                Picker?.Request(new PickRequest(InitialMousePosition.X, InitialMousePosition.Y, PickIntent.Select, GetPickModifiers()));
            }
        }

        protected override void OnMouseDown(object? sender, MouseEventArgs e)
        {
            base.OnMouseDown(sender, e);

            if (EditorState.Mode != EditorMode.Viewer)
            {
                return;
            }

            if (e.Button == MouseButtons.Left)
            {
                if (e.Clicks == 2)
                {
                    var modifiers = GetPickModifiers();
                    var intent = modifiers.HasFlag(PickModifiers.Control)
                        ? PickIntent.Open
                        : PickIntent.Details;
                    Picker?.Request(new PickRequest(e.X, e.Y, intent, modifiers));
                }
            }
        }

        // Read when the click happens, since the pick only resolves on a later frame
        private static PickModifiers GetPickModifiers()
        {
            var keys = Control.ModifierKeys;
            var modifiers = PickModifiers.None;

            if ((keys & Keys.Control) != 0)
            {
                modifiers |= PickModifiers.Control;
            }

            if ((keys & Keys.Shift) != 0)
            {
                modifiers |= PickModifiers.Shift;
            }

            if ((keys & Keys.Alt) != 0)
            {
                modifiers |= PickModifiers.Alt;
            }

            return modifiers;
        }

        protected override void OnGLLoad()
        {
            base.OnGLLoad();

            ReportLoadingStatus("Preparing renderer…");

            frametimeQuery1 = GraphicsDevice.CreateQuery(QueryTarget.TimeElapsed, "Frame Time Query");
            frametimeQuery2 = GraphicsDevice.CreateQuery(QueryTarget.TimeElapsed, "Frame Time Query");

            // Needed to fix crash on certain drivers
            GL.BeginQuery(QueryTarget.TimeElapsed, frametimeQuery2);
            GL.EndQuery(QueryTarget.TimeElapsed);

            TextRenderer.Load();
            Renderer.Postprocess.Load(NumSamples);

            Renderer.Postprocess.FullScreenGamma = 2.01f; // 100% Brightness
            Renderer.Postprocess.ExposureCompensation = -0.4f; // eyeballed

            baseGrid = new InfiniteGrid(Scene);
            selectionVisuals = new(Scene.RendererContext, Selection);
            Picker = new(Scene.RendererContext, OnPicked);

            QuadOverdrawRenderer = new(Scene.RendererContext);
            QuadOverdrawRenderer.Load();

            Renderer.ShadowTextureSize = Settings.Config.ShadowResolution;
            Renderer.Initialize();

            Renderer.MainFramebuffer = MainFramebuffer;

            MainFramebuffer!.Bind(FramebufferTarget.Framebuffer);

            var timer = Stopwatch.StartNew();
            PreSceneLoad();
            LoadScene();
            timer.Stop();
            Log.Debug(GetType().Name, $"Loading scene time: {timer.Elapsed}, shader variants: {Scene.RendererContext.ShaderLoader.ShaderCount}, materials: {Scene.RendererContext.MaterialLoader.MaterialCount}");

            ReportLoadingStatus("Initializing scene…");

            PostSceneLoad();

            GuiContext.ClearCache();
            GuiContext.GLPostLoadAction?.Invoke(this);
            GuiContext.GLPostLoadAction = null;
        }

        /// <summary>
        /// Renders one full frame with culling disabled so the driver specializes every
        /// (program, vertex layout, framebuffer) combination once.
        ///
        /// Must run on the render loop thread. Nvidia specializes per thread, so a frame drawn while the
        /// context still belongs to the loading thread specializes nothing the render loop can use, and every
        /// program pays for it again on its first real draw.
        /// </summary>
        private void PrewarmDrawCalls()
        {
            Debug.Assert(MainFramebuffer != null);

            Scene.RendererContext.ShaderLoader.LinkLoadedShaders();
            Renderer.DisableAllCulling = true;

            Renderer.Camera.CopyFrom(Input.Camera);
            Renderer.Prewarming = true;

            try
            {
                // A non-zero delta so that particles actually simulate
                OnPaint(1f / 60f);

                foreach (var particleNode in Scene.AllNodes.OfType<ParticleSceneNode>())
                {
                    particleNode.Prewarm(Renderer.Camera);
                }
            }
            finally
            {
                Renderer.DisableAllCulling = false;
                Renderer.Prewarming = false;
            }
        }

        protected void ReportLoadingStatus(string status) => GuiContext.LoadingProgress?.Report(status);

        protected override void PrewarmRenderer()
        {
            ReportLoadingStatus("Compiling shaders…");

            var start = Stopwatch.GetTimestamp();

            PrewarmDrawCalls();

            Log.Debug(GetType().Name, $"Prewarm time: {Stopwatch.GetElapsedTime(start)}");
        }

        /// <summary>
        /// Creates <see cref="soundPlayer"/> and loads the game's sound events, wiring up the master volume from
        /// settings and the default mix group volumes. Safe to call once; failures (e.g. no audio device) are logged
        /// and leave <see cref="soundPlayer"/> null. Intended for scene viewers that want to play scene audio.
        /// </summary>
        protected void InitializeSoundPlayer()
        {
            if (soundPlayer != null)
            {
                return;
            }

            try
            {
                // The player takes ownership of the device and disposes it in its own Dispose (called from ours);
                // CA2000 cannot see ownership transfer through the constructor, so this is not actually a leak.
#pragma warning disable CA2000
                soundPlayer = new SoundEventPlayer(GuiContext, new NAudioDevice(), Scene.RendererContext.Logger);
#pragma warning restore CA2000
            }
            catch (COMException e)
            {
                // WASAPI has no usable render endpoint (no audio hardware, headless/RDP session, audio service off).
                // This is an expected environment, not a bug: run without sound rather than failing the viewer.
                Log.Warn(nameof(GLSceneViewer), $"No audio device available, sound playback disabled: {e.Message}");
                return;
            }

            soundPlayer.LoadSoundEvents();
            soundPlayer.LoadSoundscapes();

            // todo: collision filter 'default' and 'blocksound'
            // const float OcclusionEndMargin = 48f;
            // soundPlayer.OcclusionTrace = (listener, sound) =>
            //     Scene.PhysicsWorld?.TraceRay(listener, sound) is { Hit: true } hit
            //         && Vector3.DistanceSquared(hit.HitPosition, sound) > OcclusionEndMargin * OcclusionEndMargin;

            soundPlayer.Suspended = true; // start with fade-in
            soundPlayer.Volume = Settings.Config.Volume;
            soundPlayer.MixGroupVolume["Weapons"] = 0.7f;
            soundPlayer.MixGroupVolume["Foley"] = 0.5f;
            soundPlayer.MixGroupVolume["Footsteps"] = 0.4f;
            soundPlayer.MixGroupVolume["PlayerDamage"] = 0.4f;
            soundPlayer.DefaultMixGroupVolume = 0.1f;
        }

        public override void OnDetachedFromRenderLoop()
        {
            base.OnDetachedFromRenderLoop();
            soundPlayer?.Suspended = true;
        }

        protected override void OnUpdate(float frameTime)
        {
            base.OnUpdate(frameTime);

            if (soundPlayer != null)
            {
                soundPlayer.Volume = Settings.Config.Volume;
                soundPlayer.Suspended = Paused;
            }

            Input.EnableMouseLook = true;

            if (loadedDefaultLighting && Input.NoClip && (CurrentlyPressedKeys & TrackedKeys.Control) != 0)
            {
                var delta = new Vector2(LastMouseDelta.Y, LastMouseDelta.X);

                sunAngles += delta;
                Scene.AdjustEnvMapSunAngle(Matrix4x4.CreateRotationZ(-delta.Y / 80f));
                UpdateSunAngles();
                Scene.UpdateBuffers();
                Input.EnableMouseLook = false;
            }

            EditorState.Update();

            if (visibilityDirty)
            {
                visibilityDirty = false;
                ApplyVisibility();
            }

            // Walk mode keeps simulating while the cursor is over the ui, otherwise player
            // physics and teleports stay frozen until the mouse moves back over the viewport.
            if (MouseOverRenderArea || Input.ForceUpdate || Input.WalkMode)
            {
                Input.MouseSensitivity = Settings.Config.MouseSensitivity;
                Input.SmoothCameraEnabled = Settings.Config.SmoothCameraEnabled;

                var pressedKeys = ConsumeCurrentlyPressedKeysForUpdate();
                var modifierKeys = Control.ModifierKeys;

                if ((modifierKeys & Keys.Shift) > 0)
                {
                    pressedKeys |= TrackedKeys.Shift;
                }

                if ((modifierKeys & Keys.Alt) > 0)
                {
                    pressedKeys |= TrackedKeys.Alt;
                }

                var mouseDelta = ConsumePendingMouseDelta();
                var wheelDelta = ConsumePendingMouseWheelDelta();

                Input.MouseSensitivity = Settings.Config.MouseSensitivity;
                EditorState.HandleKeys(pressedKeys);
                Input.Tick(frameTime, pressedKeys, new Vector2(mouseDelta.X, mouseDelta.Y), Renderer.Camera);
                LastMouseDelta = mouseDelta;

                // Walk mode and mouse look aim with the mouse, so they hold the cursor. Leaving both,
                // pausing, escape, or the viewport losing focus hands it back.
                var wantsMouseLook = (Input.WalkMode || Input.MouseLook) && !Paused && !MouseReleased;

                // Taking the cursor needs it over the viewport, but keeping it does not, or a fast
                // look that outran the pointer would drop the grab on its way past the edge.
                var alreadyHoldingCursor = GrabbedMouse;

                GrabbedMouse = wantsMouseLook && (alreadyHoldingCursor || MouseOverRenderArea);
            }
        }

        /// <summary>
        /// Advances the sound system and reports its cost. Runs inside the frame's timing bracket rather
        /// than in <see cref="OnUpdate"/>, which is outside it, so the listener update shows up as a row.
        /// </summary>
        private void UpdateSoundPlayer()
        {
            if (soundPlayer == null)
            {
                return;
            }

            if (!Paused)
            {
                using (new ProfilerScope("Update Sounds"))
                {
                    soundPlayer.Update(Renderer.Camera);
                }
            }
        }

        protected void DrawLowerCornerText(ValveResourceFormat.Renderer.TextRenderer.TextMemory text, Color32 color, int lineFromBottom = 0)
        {
            Debug.Assert(MainFramebuffer != null);

            TextRenderer.AddText(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
            {
                X = 2f,
                Y = MainFramebuffer.Height - 4f - lineFromBottom * 16f,
                Scale = 14f,
                Color = color,
                Text = text
            });
        }

        protected void DrawWorldSpaceText(string text, float size, Vector3 position, Color32 color, Scene.RenderContext renderContext)
        {
            Scene.WantsSceneDepth = true;
            TextRenderer.AddTextBillboard(position, new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
            {
                Scale = size,
                Color = color,
                Text = text,
                CenterVertical = true,
                CenterHorizontal = true,
            }, renderContext.Camera, depthMask: true);
        }

        protected override void BlitFramebufferToScreen()
        {
            Debug.Assert(MainFramebuffer != null);
            Debug.Assert(GLDefaultFramebuffer != null);

            Renderer.PostprocessRender(MainFramebuffer, GLDefaultFramebuffer);
        }

        protected override void OnBufferSwapped(double blockedMs, double framePeriodMs)
        {
            Renderer.PerfStats.Timings.SetBufferSwapTime(blockedMs, framePeriodMs);
        }

        protected override void OnPaint(float frameTime)
        {
            Debug.Assert(MainFramebuffer != null);
            Debug.Assert(Picker != null);
            Debug.Assert(selectionVisuals != null);

            Renderer.PerfStats.Capture = perfDisplay == PerfDisplay.Stats;
            Renderer.PerfStats.Timings.Capture = perfDisplay == PerfDisplay.Timings;
            Renderer.PerfStats.Allocations.Capture = perfDisplay == PerfDisplay.Allocations;

            Renderer.PerfStats.MarkFrameBegin();
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

                Input.LateUpdate(Renderer.Camera);

                if (ScreenDebugText.Length > 0)
                {
                    TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                    {
                        X = 0.005f,
                        Y = 0.03f,
                        Scale = 14f,
                        Text = ScreenDebugText,
                    }, Renderer.Camera);
                }

                selectionVisuals.Update(renderContext, updateContext);
            }

            // After the update, so the listener is placed with this frame's camera vectors rather than
            // this frame's position and last frame's facing
            UpdateSoundPlayer();

            Renderer.ForceResolveSceneDepth = ShowBaseGrid;

            var quadOverdrawThisFrame = false;

            using (new GLDebugGroup("Scenes Render"))
            {
                Picker.Render(Renderer, renderContext);

                if (Picker.Texture.IsDebugActive)
                {
                    renderContext.ReplacementShader = Picker.Texture.DebugShader;
                }
                else if (QuadOverdrawRenderer?.IsActive == true)
                {
                    QuadOverdrawRenderer.Prepare(MainFramebuffer.Width, MainFramebuffer.Height);

                    quadOverdrawThisFrame = true;
                }

                Renderer.Render(renderContext);

                if (quadOverdrawThisFrame)
                {
                    using (new GLDebugGroup("Quad Overdraw Counting Pass"))
                    {
                        QuadOverdrawRenderer!.BeginCountingPass(MainFramebuffer);

                        renderContext.OverdrawShader = QuadOverdrawRenderer.SceneShader;
                        Renderer.RenderScenesWithView(renderContext);
                        renderContext.OverdrawShader = null;

                        QuadOverdrawRenderer.EndCountingPass(MainFramebuffer);
                    }

                    QuadOverdrawRenderer!.Render();
                }
            }

            using (new GLDebugGroup("Lines Render"))
            {
                selectionVisuals.Render();

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

                if (showPhysicsTraces && Renderer.EntitySystem.PhysicsWorld != null)
                {
                    physicsTraceRenderer ??= new PhysicsTraceDebugRenderer(Scene.RendererContext);
                    physicsTraceRenderer.Render(Renderer.EntitySystem.PhysicsWorld, Input, Renderer.Camera);
                }

                if (ShowBaseGrid && baseGrid != null)
                {
                    baseGrid.Render();

                    DrawWorldSpaceText("+X", 10f, Vector3.UnitX * 120f, Color32.Red, renderContext);
                    DrawWorldSpaceText("-X", 10f, -Vector3.UnitX * 120f, Color32.Red, renderContext);
                    DrawWorldSpaceText("+Y", 10f, Vector3.UnitY * 120f, Color32.Green, renderContext);
                    DrawWorldSpaceText("-Y", 10f, -Vector3.UnitY * 120f, Color32.Green, renderContext);
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

                // Zero length frames (the first frame after resuming) would inflate the average.
                if (frameTime > 0f)
                {
                    frameTimes[frameTimeNextId++] = frameTime;
                    frameTimeNextId %= frameTimes.Length;
                    frameTimeCount = Math.Min(frameTimeCount + 1, frameTimes.Length);
                }

                if (frameTimeCount > 0 && fpsElapsed >= FpsUpdateTimeSpan)
                {
                    var frametimeQuery = frametimeQuery2;
                    frametimeQuery2 = frametimeQuery1;
                    frametimeQuery1 = frametimeQuery;

                    GL.GetQueryObject(frametimeQuery, GetQueryObjectParam.QueryResultNoWait, out long gpuTime);
                    var gpuFrameTime = gpuTime / 1_000_000f;

                    var frameTimeSum = 0f;

                    // Only the samples written so far, the rest of the ring is still zeroed.
                    for (var i = 0; i < frameTimeCount; i++)
                    {
                        frameTimeSum += frameTimes[i];
                    }

                    var fps = frameTimeCount / frameTimeSum;
                    var cpuFrameTime = Stopwatch.GetElapsedTime(LastUpdate, currentTime).TotalMilliseconds;

                    lastFpsUpdate = currentTime;
                    fpsText.Format($"FPS: {fps,-3:0}  CPU: {cpuFrameTime,-4:0.0}ms  GPU: {gpuFrameTime,-4:0.0}ms");
                }

                DrawLowerCornerText(fpsText, Color32.White);
            }

            BlitFramebufferToScreen();

            if (Input.ShowCrosshair)
            {
                crosshairRenderer.Render(Renderer.Camera);
            }

            if (ShowsEditorMode)
            {
                var isGame = EditorState.Mode == EditorMode.Game;

                TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 0.5f,
                    Y = 0.03f,
                    Scale = 14f,
                    Color = isGame ? new Color32(90, 220, 90) : new Color32(120, 180, 255),
                    Text = isGame ? "GAME" : "VIEWER",
                    CenterHorizontal = true,
                }, Renderer.Camera);
            }

            if (GrabbedMouse && ShowSpeed)
            {
                TextRenderer.AddTextRelative(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                {
                    X = 0.5f,
                    Y = 0.85f,
                    Scale = 12f,
                    Color = Color32.Yellow,
                    Text = speedText.Format($"Speed: {Input.Velocity.AsVector2().Length():0.0} u/s"),
                    CenterHorizontal = true,
                }, Renderer.Camera);
            }

            if (showVisDebug && Scene.VoxelVisibility != null)
            {
                var pvsPos = Renderer.LockedCullPosition ?? Renderer.Camera.Location;
                var cluster = Scene.VoxelVisibility.GetClusterForPosition(pvsPos);
                var y = 18f;

                void AddLine(string text, Color32 color)
                {
                    TextRenderer.AddText(new ValveResourceFormat.Renderer.TextRenderer.TextRenderRequest
                    {
                        X = 4f,
                        Y = y,
                        Scale = 14f,
                        Color = color,
                        Text = text,
                    });
                    y += 16f;
                }

                AddLine(
                    cluster < 0 ? "No PVS at this position" : $"PVS cluster {cluster}",
                    cluster < 0 ? new Color32(255, 0, 0) : Color32.White
                );

                if (!Scene.CurrentFramePvs.IsEmpty)
                {
                    var visCount = 0;

                    foreach (var b in Scene.CurrentFramePvs.Span)
                    {
                        visCount += BitOperations.PopCount(b);
                    }

                    AddLine($"PVS visible: {visCount}/{Scene.VoxelVisibility.ClusterCount} clusters", Color32.White);
                }
            }

            if (perfDisplay == PerfDisplay.Stats)
            {
                Renderer.PerfStats.DisplayStats(TextRenderer, Renderer.Camera, Scene, SkyboxScene);
            }
            else if (perfDisplay == PerfDisplay.Timings)
            {
                Renderer.PerfStats.Timings.DisplayTimings(TextRenderer, Renderer.Camera);
            }
            else if (perfDisplay == PerfDisplay.Allocations)
            {
                Renderer.PerfStats.Allocations.DisplayAllocations(TextRenderer, Renderer.Camera);
            }

            TextRenderer.Render(Renderer.Camera, Renderer.ResolvedSceneDepth);
            Picker?.DispatchResults();

            Renderer.PerfStats.MarkFrameEnd();
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
                var supportedRenderModes = new HashSet<string>(Picker.Texture.Shader.RenderModes);

                if (QuadOverdrawRenderer != null)
                {
                    supportedRenderModes.UnionWith(QuadOverdrawRenderer.SceneShader.RenderModes);
                }

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

        /// <summary>Shows the chosen layers from the next frame, less those the tools visibility hides in viewers with an editor mode.</summary>
        protected void SetEnabledLayers(HashSet<string> layers)
        {
            chosenLayers = layers;
            RequestVisibilityUpdate();
        }

        /// <summary>
        /// Reapplies what is shown at the start of the next frame. Nodes are added and removed on the render
        /// thread while the world runs, so they are only walked there.
        /// </summary>
        protected void RequestVisibilityUpdate() => visibilityDirty = true;

        /// <summary>Applies the chosen layers and, in viewers with an editor mode, <see cref="EditorState.Tools"/>. Runs on the render thread.</summary>
        protected virtual void ApplyVisibility()
        {
            if (ShowsEditorMode)
            {
                foreach (var scene in Renderer.Scenes)
                {
                    scene.ShowToolsMaterials = EditorState.Tools.ToolMaterialsVisible;
                }
            }

            if (chosenLayers is not { } layers)
            {
                return;
            }

            var enabledLayers = ShowsEditorMode ? EditorState.Tools.FilterLayers(layers) : layers;

            foreach (var scene in Renderer.Scenes)
            {
                scene.SetEnabledLayers(enabledLayers);
            }
        }

        // Only shown in the Cubemaps render mode; the choice is kept for when that mode is selected again
        private void ApplyCubemapColors()
        {
            var mode = isCubemapsRenderMode && cubemapColorsComboBox != null
                ? (EnvMapDebugColorsMode)Math.Max(cubemapColorsComboBox.SelectedIndex, 0)
                : EnvMapDebugColorsMode.Off;

            foreach (var scene in Renderer.Scenes)
            {
                scene.LightingDebug.EnvMapColors = mode;
            }
        }

        private void SetRenderMode(string renderMode)
        {
            Debug.Assert(Picker != null);
            Debug.Assert(selectionVisuals != null);

            Renderer.ViewBuffer!.Data!.RenderMode = RenderModes.GetShaderId(renderMode);

            Renderer.Postprocess.Enabled = Renderer.ViewBuffer.Data.RenderMode == 0;

            foreach (var scene in Renderer.Scenes)
            {
                scene.EnableCompaction = renderMode != "Meshlets";
            }

            Picker.Texture.SetRenderMode(renderMode);
            QuadOverdrawRenderer?.SetRenderMode(renderMode);

            isCubemapsRenderMode = renderMode == "Cubemaps";

            // The label and the box together, so the row is gone rather than empty
            cubemapColorsComboBox?.Parent!.Visible = isCubemapsRenderMode;

            ApplyCubemapColors();

            selectionVisuals.LightingBindings = renderMode switch
            {
                "Cubemaps" => LightingBindingDisplay.EnvMaps,
                "Irradiance" or "Illumination" => LightingBindingDisplay.LightProbe,
                _ => LightingBindingDisplay.None,
            };

            foreach (var node in Renderer.Scenes.SelectMany(static scene => scene.AllNodes))
            {
                node.SetRenderMode(renderMode);
            }
        }

        protected override void OnKeyDown(Keys keyData)
        {
            if (keyData == Keys.Delete)
            {
                Selection.ToggleLayerEnabled();
                return;
            }

            if (keyData == Keys.Escape)
            {
                Selection.Clear();
                if (Input.WalkMode)
                {
                    MouseReleased = true;
                }
            }

            if (keyData == Keys.Tab && perfDisplayComboBox != null)
            {
                // Cycle through the perf display modes (the callback updates perfDisplay)
                perfDisplayComboBox.SelectedIndex = (perfDisplayComboBox.SelectedIndex + 1) % perfDisplayComboBox.Items.Count;
            }

            base.OnKeyDown(keyData);
        }

#if DEBUG
        private void OnHotReload(object? sender, string? e)
        {
            using var lockedGl = MakeCurrent();

            if (renderModeComboBox != null)
            {
                SetAvailableRenderModes(true);
            }

            foreach (var node in Renderer.Scenes.SelectMany(static scene => scene.AllNodes))
            {
                node.UpdateVertexArrayObjects();
            }

            GLControl?.Invalidate();
        }
#endif
    }
}

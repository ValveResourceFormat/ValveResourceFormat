using System.Diagnostics;
using System.Drawing;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal abstract class GLSceneViewer : GLViewerControl, IDisposable
    {
        public Scene Scene { get; }
        public Scene SkyboxScene { get; protected set; }
        public SceneSkybox2D Skybox2D { get; protected set; }
        public VrfGuiContext GuiContext => Scene.GuiContext;

        private bool ShowBaseGrid;
        private bool ShowLightBackground;
        public bool ShowSkybox { get; set; } = true;
        public bool IsWireframe { get; set; }
        public bool EnableOcclusionCulling { get; set; }

        public float Uptime { get; private set; }

        private bool showStaticOctree;
        private bool showDynamicOctree;
        private Frustum lockedCullFrustum;

        protected UniformBuffer<ViewConstants> viewBuffer;
        public List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; } = [];

        private readonly List<RenderModes.RenderMode> renderModes = new(RenderModes.Items.Count);
        private int renderModeCurrentIndex;
        private Font renderModeBoldFont;
        private ComboBox renderModeComboBox;
        private InfiniteGrid baseGrid;
        private SceneBackground baseBackground;
        private OctreeDebugRenderer<SceneNode> staticOctreeRenderer;
        private OctreeDebugRenderer<SceneNode> dynamicOctreeRenderer;
        protected SelectedNodeRenderer selectedNodeRenderer;

        public enum DepthOnlyProgram
        {
            Static,
            StaticAlphaTest,
            Animated,
            AnimatedEightBones,
            OcclusionQueryAABBProxy,
        }
        private readonly Shader[] depthOnlyShaders = new Shader[Enum.GetValues<DepthOnlyProgram>().Length];
        public Framebuffer ShadowDepthBuffer { get; private set; }

        protected GLSceneViewer(VrfGuiContext guiContext, Frustum cullFrustum) : base(guiContext)
        {
            Scene = new Scene(guiContext);
            lockedCullFrustum = cullFrustum;

            InitializeControl();
            AddWireframeToggleControl();

            GLLoad += OnLoad;

#if DEBUG
            guiContext.ShaderLoader.ShaderHotReload.ReloadShader += OnHotReload;
#endif
        }

        protected GLSceneViewer(VrfGuiContext guiContext) : base(guiContext)
        {
            Scene = new Scene(guiContext);

            InitializeControl();
            AddCheckBox("Lock Cull Frustum", false, (v) =>
            {
                lockedCullFrustum = v ? Camera.ViewFrustum.Clone() : null;
            });
            AddCheckBox("Show Static Octree", showStaticOctree, (v) =>
            {
                showStaticOctree = v;

                if (showStaticOctree)
                {
                    staticOctreeRenderer.StaticBuild();
                }
            });
            AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);
            AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
            {
                Scene.ShowToolsMaterials = v;

                if (SkyboxScene != null)
                {
                    SkyboxScene.ShowToolsMaterials = v;
                }
            });

            AddWireframeToggleControl();

            GLLoad += OnLoad;

#if DEBUG
            guiContext.ShaderLoader.ShaderHotReload.ReloadShader += OnHotReload;
#endif
        }

        protected override void Dispose(bool disposing)
        {
            base.Dispose(disposing);

            if (disposing)
            {
                viewBuffer?.Dispose();
                Scene?.Dispose();
                SkyboxScene?.Dispose();

                GLPaint -= OnPaint;

                if (renderModeComboBox != null)
                {
                    renderModeComboBox.DrawItem -= OnRenderModeDrawItem;
                    renderModeComboBox.Dispose();
                    renderModeComboBox = null;
                }

                if (renderModeBoldFont != null)
                {
                    renderModeBoldFont.Dispose();
                    renderModeBoldFont = null;
                }
#if DEBUG
                GuiContext.ShaderLoader.ShaderHotReload.ReloadShader -= OnHotReload;
#endif
            }
        }

        protected abstract void InitializeControl();

        private void CreateBuffers()
        {
            viewBuffer = new(ReservedBufferSlots.View);
        }

        void UpdatePerViewGpuBuffers(Scene scene, Camera camera)
        {
            camera.SetViewConstants(viewBuffer.Data);
            scene.SetFogConstants(viewBuffer.Data);
            viewBuffer.Update();

            postProcessRenderer.State = scene.PostProcessInfo.CurrentState;
            postProcessRenderer.TonemapScalar = scene.PostProcessInfo.CalculateTonemapScalar();
        }

        public virtual void PreSceneLoad()
        {
            const string vtexFileName = "ggx_integrate_brdf_lut_schlick.vtex_c";
            var assembly = Assembly.GetExecutingAssembly();

            // Load brdf lut, preferably from game.
            var brdfLutResource = GuiContext.LoadFile("textures/dev/" + vtexFileName);

            try
            {
                Stream brdfStream; // Will be used by LoadTexture, and disposed by resource

                if (brdfLutResource == null)
                {
                    brdfStream = assembly.GetManifestResourceStream("GUI.Utils." + vtexFileName);

                    brdfLutResource = new Resource() { FileName = vtexFileName };
                    brdfLutResource.Read(brdfStream);
                }

                // TODO: add annoying force clamp for lut
                Textures.Add(new(ReservedTextureSlots.BRDFLookup, "g_tBRDFLookup", GuiContext.MaterialLoader.LoadTexture(brdfLutResource)));
            }
            finally
            {
                brdfLutResource?.Dispose();
            }

            // Load default cube fog texture.
            using var cubeFogStream = assembly.GetManifestResourceStream("GUI.Utils.sky_furnace.vtex_c");
            using var cubeFogResource = new Resource() { FileName = "default_cube.vtex_c" };
            cubeFogResource.Read(cubeFogStream);

            var defaultCubeTexture = GuiContext.MaterialLoader.LoadTexture(cubeFogResource);
            Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", defaultCubeTexture));


            const string blueNoiseName = "blue_noise_256.vtex_c";
            var blueNoiseResource = GuiContext.LoadFile("textures/dev/" + blueNoiseName);

            try
            {
                Stream blueNoiseStream; // Same method as brdf

                if (blueNoiseResource == null)
                {
                    blueNoiseStream = assembly.GetManifestResourceStream("GUI.Utils." + blueNoiseName);

                    blueNoiseResource = new Resource() { FileName = blueNoiseName };
                    blueNoiseResource.Read(blueNoiseStream);
                }

                // only needed here for now, move to GLSceneViewer
                postProcessRenderer.BlueNoise = GuiContext.MaterialLoader.LoadTexture(blueNoiseResource);
            }
            finally
            {
                blueNoiseResource?.Dispose();
            }

        }

        public virtual void PostSceneLoad()
        {
            Scene.Initialize();
            SkyboxScene?.Initialize();

            if (Scene.FogInfo.CubeFogActive)
            {
                Textures.RemoveAll(t => t.Slot == ReservedTextureSlots.FogCubeTexture);
                Textures.Add(new(ReservedTextureSlots.FogCubeTexture, "g_tFogCubeTexture", Scene.FogInfo.CubemapFog.CubemapFogTexture));
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

                Camera.SetLocation(location);
                Camera.LookAt(bbox.Center);
            }

            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
            dynamicOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.DynamicOctree, Scene.GuiContext, true);

            SetAvailableRenderModes();
        }

        protected abstract void LoadScene();

        protected abstract void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo);

        protected virtual void OnLoad(object sender, EventArgs e)
        {
            baseGrid = new InfiniteGrid(Scene);
            Skybox2D = baseBackground = new SceneBackground(Scene);
            selectedNodeRenderer = new(Scene, textRenderer);

            Picker = new PickingTexture(Scene.GuiContext, OnPicked);

            var shadowQuality = Settings.Config.ShadowResolution;

            ShadowDepthBuffer = Framebuffer.Prepare(shadowQuality, shadowQuality, 0, null, Framebuffer.DepthAttachmentFormat.Depth32F);
            ShadowDepthBuffer.Initialize();
            ShadowDepthBuffer.ClearMask = ClearBufferMask.DepthBufferBit;
            GL.DrawBuffer(DrawBufferMode.None);
            GL.ReadBuffer(ReadBufferMode.None);
            Textures.Add(new(ReservedTextureSlots.ShadowDepthBufferDepth, "g_tShadowDepthBufferDepth", ShadowDepthBuffer.Depth));

            GL.TextureParameter(ShadowDepthBuffer.Depth.Handle, TextureParameterName.TextureBaseLevel, 0);
            GL.TextureParameter(ShadowDepthBuffer.Depth.Handle, TextureParameterName.TextureMaxLevel, 0);
            GL.TextureParameter(ShadowDepthBuffer.Depth.Handle, TextureParameterName.TextureCompareMode, (int)TextureCompareMode.CompareRToTexture);
            ShadowDepthBuffer.Depth.SetFiltering(TextureMinFilter.Linear, TextureMagFilter.Linear);
            ShadowDepthBuffer.Depth.SetWrapMode(TextureWrapMode.ClampToBorder);

            depthOnlyShaders[(int)DepthOnlyProgram.Static] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only");
            //depthOnlyShaders[(int)DepthOnlyProgram.StaticAlphaTest] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "F_ALPHA_TEST", 1 } });
            depthOnlyShaders[(int)DepthOnlyProgram.Animated] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "D_ANIMATED", 1 } });
            depthOnlyShaders[(int)DepthOnlyProgram.AnimatedEightBones] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only", new Dictionary<string, byte> { { "D_ANIMATED", 1 }, { "D_EIGHT_BONE_BLENDING", 1 } });

            depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy] = GuiContext.ShaderLoader.LoadShader("vrf.depth_only_aabb");

            MainFramebuffer.Bind(FramebufferTarget.Framebuffer);
            CreateBuffers();

            var timer = Stopwatch.StartNew();
            PreSceneLoad();
            LoadScene();
            timer.Stop();
            Log.Debug(GetType().Name, $"Loading scene time: {timer.Elapsed}, shader variants: {GuiContext.ShaderLoader.ShaderCount}, materials: {GuiContext.MaterialLoader.MaterialCount}");

            PostSceneLoad();

            GLLoad -= OnLoad;
            GLPaint += OnPaint;

            GuiContext.ClearCache();

            if (GuiContext.GLPostLoadAction != null)
            {
                GuiContext.GLPostLoadAction.Invoke(this);
                GuiContext.GLPostLoadAction = null;
            }
        }

        protected virtual void OnPaint(object sender, RenderEventArgs e)
        {
            Uptime += e.FrameTime;
            viewBuffer.Data.Time = Uptime;

            var renderContext = new Scene.RenderContext
            {
                View = this,
                Camera = Camera,
                Framebuffer = MainFramebuffer,
            };

            using (new GLDebugGroup("Update Loop"))
            {
                Scene.Update(e.FrameTime);
                SkyboxScene?.Update(e.FrameTime);

                Scene.PostProcessInfo.UpdatePostProcessing(Camera);

                selectedNodeRenderer.Update(new Scene.UpdateContext(e.FrameTime));

                Scene.SetupSceneShadows(Camera, ShadowDepthBuffer.Width);
                Scene.CollectSceneDrawCalls(Camera, lockedCullFrustum);
                SkyboxScene?.CollectSceneDrawCalls(Camera, lockedCullFrustum);
            }

            using (new GLDebugGroup("Scenes Render"))
            {
                if (Picker.ActiveNextFrame)
                {
                    using var _ = new GLDebugGroup("Picker Object Id Render");
                    renderContext.ReplacementShader = Picker.Shader;
                    renderContext.Framebuffer = Picker;

                    RenderScenesWithView(renderContext);
                    Picker.Finish();
                }
                else if (Picker.DebugShader is not null)
                {
                    renderContext.ReplacementShader = Picker.DebugShader;
                }

                RenderSceneShadows(renderContext);
                RenderScenesWithView(renderContext);
            }

            using (new GLDebugGroup("Lines Render"))
            {
                selectedNodeRenderer.Render(renderContext);

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
        }

        protected void DrawMainScene()
        {
            var renderContext = new Scene.RenderContext
            {
                View = this,
                Camera = Camera,
                Framebuffer = MainFramebuffer,
                Scene = Scene,
            };

            UpdatePerViewGpuBuffers(Scene, Camera);
            Scene.SetSceneBuffers();

            Scene.RenderOpaqueLayer(renderContext);
            RenderTranslucentLayer(Scene, renderContext);
        }

        private void RenderSceneShadows(Scene.RenderContext renderContext)
        {
            GL.Viewport(0, 0, ShadowDepthBuffer.Width, ShadowDepthBuffer.Height);
            ShadowDepthBuffer.Bind(FramebufferTarget.Framebuffer);
            GL.DepthRange(0, 1);
            GL.Clear(ClearBufferMask.DepthBufferBit);

            renderContext.Framebuffer = ShadowDepthBuffer;
            renderContext.Scene = Scene;

            viewBuffer.Data.ViewToProjection = Scene.LightingInfo.SunViewProjection;
            var worldToShadow = Scene.LightingInfo.SunViewProjection;
            viewBuffer.Data.WorldToShadow = worldToShadow;
            viewBuffer.Data.SunLightShadowBias = Scene.LightingInfo.SunLightShadowBias;
            viewBuffer.Update();

            Scene.RenderOpaqueShadows(renderContext, depthOnlyShaders);
        }

        private void RenderScenesWithView(Scene.RenderContext renderContext)
        {
            GL.Viewport(0, 0, renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);
            renderContext.Framebuffer.BindAndClear();

            // TODO: check if renderpass allows wireframe mode
            // TODO+: replace wireframe shaders with solid color
            if (IsWireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }

            GL.DepthRange(0.05, 1);

            UpdatePerViewGpuBuffers(Scene, Camera);
            Scene.SetSceneBuffers();

            using (new GLDebugGroup("Main Scene Opaque Render"))
            {
                renderContext.Scene = Scene;
                Scene.RenderOpaqueLayer(renderContext);
            }

            using (new GLDebugGroup("Occlusion Tests"))
            {
                Scene.RenderOcclusionProxies(renderContext, depthOnlyShaders[(int)DepthOnlyProgram.OcclusionQueryAABBProxy]);
            }

            using (new GLDebugGroup("Sky Render"))
            {
                GL.DepthRange(0, 0.05);

                renderContext.ReplacementShader?.SetUniform1("isSkybox", 1u);
                var render3DSkybox = ShowSkybox && SkyboxScene != null;

                if (render3DSkybox)
                {
                    SkyboxScene.SetSceneBuffers();
                    renderContext.Scene = SkyboxScene;

                    using var _ = new GLDebugGroup("3D Sky Scene");
                    SkyboxScene.RenderOpaqueLayer(renderContext);
                }

                using (new GLDebugGroup("2D Sky Render"))
                {
                    Skybox2D.Render();
                }

                if (render3DSkybox)
                {
                    using (new GLDebugGroup("3D Sky Scene Translucent Render"))
                    {
                        RenderTranslucentLayer(SkyboxScene, renderContext);
                    }

                    // Back to main scene.
                    Scene.SetSceneBuffers();
                    renderContext.Scene = Scene;
                }

                renderContext.ReplacementShader?.SetUniform1("isSkybox", 0u);
                GL.DepthRange(0.05, 1);
            }

            using (new GLDebugGroup("Main Scene Translucent Render"))
            {
                RenderTranslucentLayer(Scene, renderContext);
            }

            if (IsWireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        private static void RenderTranslucentLayer(Scene scene, Scene.RenderContext renderContext)
        {
            GL.DepthMask(false);
            GL.Enable(EnableCap.Blend);

            scene.RenderTranslucentLayer(renderContext);

            GL.Disable(EnableCap.Blend);
            GL.DepthMask(true);
        }

        protected void AddBaseGridControl()
        {
            ShowBaseGrid = true;

            AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);
            AddCheckBox("Show Light Background", ShowLightBackground, (v) =>
            {
                ShowLightBackground = v;
                baseBackground.SetLightBackground(ShowLightBackground);
            });
        }

        protected void AddWireframeToggleControl()
        {
            AddCheckBox("Show Wireframe", false, (v) => IsWireframe = v);
        }

        protected void AddRenderModeSelectionControl()
        {
            if (renderModeComboBox != null)
            {
                return;
            }

            renderModeComboBox = AddSelection("Render Mode", (_, i) =>
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
                    renderModeComboBox.SelectedIndex = renderModeCurrentIndex > i ? (i - 1) : (i + 1);
                    return;
                }

                renderModeCurrentIndex = i;
                SetRenderMode(renderMode.Name);
            });

            renderModeBoldFont = new Font(renderModeComboBox.Font, FontStyle.Bold);
            renderModeComboBox.DrawMode = DrawMode.OwnerDrawFixed;
            renderModeComboBox.DrawItem += OnRenderModeDrawItem;
        }

        private void OnRenderModeDrawItem(object sender, DrawItemEventArgs e)
        {
            var comboBox = (ComboBox)sender;

            if (e.Index < 0)
            {
                return;
            }

            var mode = renderModes[e.Index];

            if (mode.IsHeader)
            {
                e.Graphics.FillRectangle(SystemBrushes.Window, e.Bounds);
                e.Graphics.DrawString(mode.Name, renderModeBoldFont, SystemBrushes.ControlText, e.Bounds);
            }
            else
            {
                e.DrawBackground();

                var bounds = e.Bounds;

                if (e.Index > 0 && (e.State & DrawItemState.ComboBoxEdit) == 0)
                {
                    bounds.X += 12;
                }

                var isSelected = (e.State & DrawItemState.Selected) > 0;
                var brush = isSelected ? SystemBrushes.HighlightText : SystemBrushes.ControlText;
                e.Graphics.DrawString(mode.Name, comboBox.Font, brush, bounds);

                e.DrawFocusRectangle();
            }
        }

        private void SetAvailableRenderModes(bool keepCurrentSelection = false)
        {
            if (renderModeComboBox != null)
            {
                var selectedIndex = 0;
                var currentlySelected = keepCurrentSelection ? renderModeComboBox.SelectedItem.ToString() : null;
                var supportedRenderModes = Scene.AllNodes
                    .SelectMany(r => r.GetSupportedRenderModes())
                    .Concat(Picker.Shader.RenderModes)
                    .Distinct()
                    .ToHashSet();

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
                renderModeComboBox.Items.AddRange(renderModes.Select(x => x.Name).ToArray());
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
                staticOctreeRenderer.Rebuild();
            }
        }

        private void SetRenderMode(string renderMode)
        {
            viewBuffer.Data.RenderMode = RenderModes.GetShaderId(renderMode);

            Picker.SetRenderMode(renderMode);
            selectedNodeRenderer.SetRenderMode(renderMode);

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
                selectedNodeRenderer.DisableSelectedNodes();
                return;
            }

            base.OnKeyDown(sender, e);
        }

#if DEBUG
        private void OnHotReload(object sender, string e)
        {
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
        }
#endif
    }
}

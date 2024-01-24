using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.ParticleRenderer;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using ValveResourceFormat;

namespace GUI.Types.Renderer
{
    internal abstract class GLSceneViewer : GLViewerControl, IGLViewer, IDisposable
    {
        public Scene Scene { get; }
        public Scene SkyboxScene { get; protected set; }
        public VrfGuiContext GuiContext => Scene.GuiContext;

        private bool ShowBaseGrid;
        public bool ShowSkybox { get; set; } = true;
        public bool IsWireframe { get; set; }

        public float Uptime { get; private set; }

        private bool showStaticOctree;
        private bool showDynamicOctree;
        private Frustum lockedCullFrustum;
        private Frustum skyboxLockedCullFrustum;

        protected UniformBuffer<ViewConstants> viewBuffer;
        private UniformBuffer<LightingConstants> lightingBuffer;
        public IReadOnlyList<IBlockBindableBuffer> Buffers { get; private set; }
        public List<(ReservedTextureSlots Slot, string Name, RenderTexture Texture)> Textures { get; } = [];

        private bool skipRenderModeChange;
        private ComboBox renderModeComboBox;
        private InfiniteGrid baseGrid;
        protected readonly Camera skyboxCamera = new();
        private OctreeDebugRenderer<SceneNode> staticOctreeRenderer;
        private OctreeDebugRenderer<SceneNode> dynamicOctreeRenderer;
        protected SelectedNodeRenderer selectedNodeRenderer;

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
            Scene = new Scene(guiContext)
            {
                MainCamera = Camera
            };

            InitializeControl();
            AddCheckBox("Lock Cull Frustum", false, (v) =>
            {
                if (v)
                {
                    lockedCullFrustum = Scene.MainCamera.ViewFrustum.Clone();

                    if (SkyboxScene != null)
                    {
                        skyboxLockedCullFrustum = SkyboxScene.MainCamera.ViewFrustum.Clone();
                    }
                }
                else
                {
                    lockedCullFrustum = null;
                    skyboxLockedCullFrustum = null;
                }
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
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                viewBuffer?.Dispose();
                lightingBuffer?.Dispose();

                GLPaint -= OnPaint;
            }

            base.Dispose(disposing);
        }

        protected abstract void InitializeControl();

        private void CreateBuffers()
        {
            viewBuffer = new(0);
            lightingBuffer = new(1);

            Buffers = [viewBuffer, lightingBuffer];
        }

        void UpdateSceneBuffersGpu(Scene scene, Camera camera)
        {
            camera.SetViewConstants(viewBuffer.Data);
            scene.SetFogConstants(viewBuffer.Data);
            viewBuffer.Update();

            lightingBuffer.Data = scene.LightingInfo.LightingData;
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

            Scene.FogInfo.DefaultFogTexture = GuiContext.MaterialLoader.LoadTexture(cubeFogResource);
        }

        public virtual void PostSceneLoad()
        {
            Scene.CalculateEnvironmentMaps();

            SkyboxScene?.CalculateEnvironmentMaps();

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
                var location = new Vector3(bbox.Max.Z + 1f, 0, bbox.Max.Z) * 1.5f;

                Camera.SetLocation(location);
                Camera.LookAt(bbox.Center);
            }
            else
            {
                Camera.SetLocation(new Vector3(256));
                Camera.LookAt(new Vector3(0));
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
            selectedNodeRenderer = new(Scene);

            Camera.SetViewportSize(GLControl.Width, GLControl.Height);

            Camera.Picker = new PickingTexture(Scene.GuiContext, OnPicked);

            CreateBuffers();

            var timer = Stopwatch.StartNew();
            PreSceneLoad();
            LoadScene();
            timer.Stop();
            Log.Debug(GetType().Name, $"Loading scene time: {timer.Elapsed}, shader variants: {GuiContext.ShaderLoader.ShaderCount}, materials: {GuiContext.MaterialLoader.MaterialCount}");

            PostSceneLoad();

            viewBuffer.Data.ClearColor = Settings.BackgroundColor;
            if (Scene.Sky is null)
            {
                MainFramebuffer.ClearColor = viewBuffer.Data.ClearColor;
            }

            GLLoad -= OnLoad;
            GLPaint += OnPaint;

            GuiContext.ClearCache();
        }


        protected virtual void OnPaint(object sender, RenderEventArgs e)
        {
            Uptime += e.FrameTime;
            viewBuffer.Data.Time = Uptime;

            Scene.Update(e.FrameTime);

            if (SkyboxScene != null)
            {
                SkyboxScene.Update(e.FrameTime);

                skyboxCamera.CopyFrom(Camera);
                skyboxCamera.SetScaledProjectionMatrix();
                skyboxCamera.SetLocation(Camera.Location - SkyboxScene.WorldOffset);
            }

            selectedNodeRenderer.Update(new Scene.UpdateContext(e.FrameTime));

            Scene.CollectSceneDrawCalls(Camera, lockedCullFrustum);
            SkyboxScene?.CollectSceneDrawCalls(skyboxCamera, skyboxLockedCullFrustum);

            var renderContext = new Scene.RenderContext
            {
                View = this,
                Camera = Camera,
                Framebuffer = MainFramebuffer,
            };

            if (Camera.Picker.IsActive)
            {
                renderContext.ReplacementShader = Camera.Picker.Shader;
                renderContext.Framebuffer = Camera.Picker;

                RenderScenesWithView(renderContext);
                Camera.Picker.Finish();
            }

            if (Camera.Picker.DebugShader is not null)
            {
                renderContext.ReplacementShader = Camera.Picker.DebugShader;
            }

            RenderScenesWithView(renderContext);

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
                baseGrid.Render(renderContext);
            }
        }

        private void RenderScenesWithView(Scene.RenderContext renderContext)
        {
            GL.Viewport(0, 0, renderContext.Framebuffer.Width, renderContext.Framebuffer.Height);
            renderContext.Framebuffer.Clear();

            // TODO: check if renderpass allows wireframe mode
            if (IsWireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Line);
            }

            GL.DepthRange(0.05, 1);
            UpdateSceneBuffersGpu(Scene, Camera);

            renderContext.Scene = Scene;
            Scene.RenderOpaqueLayer(renderContext);

            // 3D Sky
            GL.DepthRange(0, 0.05);
            if (ShowSkybox && SkyboxScene != null)
            {
                renderContext.Camera = skyboxCamera;

                UpdateSceneBuffersGpu(SkyboxScene, skyboxCamera);
                renderContext.Scene = SkyboxScene;

                SkyboxScene.RenderOpaqueLayer(renderContext);
                SkyboxScene.RenderTranslucentLayer(renderContext);

                // Back to main Scene
                UpdateSceneBuffersGpu(Scene, Camera);
            }

            // 2D Sky
            Scene.Sky?.Render(renderContext);
            GL.DepthRange(0.05, 1);

            Scene.RenderTranslucentLayer(renderContext);

            if (IsWireframe)
            {
                GL.PolygonMode(MaterialFace.FrontAndBack, PolygonMode.Fill);
            }
        }

        protected void AddBaseGridControl()
        {
            ShowBaseGrid = true;

            AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);
        }

        protected void AddWireframeToggleControl()
        {
            AddCheckBox("Show Wireframe", false, (v) => IsWireframe = v);
        }

        protected void AddRenderModeSelectionControl()
        {
            renderModeComboBox ??= AddSelection("Render Mode", (renderMode, _) =>
            {
                if (skipRenderModeChange)
                {
                    skipRenderModeChange = false;
                    return;
                }

                SetRenderMode(renderMode);
            });
        }

        private void SetAvailableRenderModes(int index = 0)
        {
            if (renderModeComboBox != null)
            {
                var supportedRenderModes = Scene.AllNodes
                    .SelectMany(r => r.GetSupportedRenderModes())
                    .Concat(Camera.Picker.Shader.RenderModes)
                    .Distinct()
                    .Prepend("Default Render Mode");

                renderModeComboBox.BeginUpdate();
                renderModeComboBox.Items.Clear();
                renderModeComboBox.Enabled = true;
                renderModeComboBox.Items.AddRange(supportedRenderModes.ToArray());
                skipRenderModeChange = true;
                renderModeComboBox.SelectedIndex = index;
                renderModeComboBox.EndUpdate();
            }
        }

        protected void SetEnabledLayers(HashSet<string> layers)
        {
            Scene.SetEnabledLayers(layers);
            SkyboxScene?.SetEnabledLayers(layers);

            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
        }

        private void SetRenderMode(string renderMode)
        {
            var title = Program.MainForm.Text;
            Program.MainForm.Text = "Source 2 Viewer - Reloading shaders…";

            try
            {
                Camera?.Picker.SetRenderMode(renderMode);
                Scene.Sky?.SetRenderMode(renderMode);
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
            finally
            {
                Program.MainForm.Text = title;
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

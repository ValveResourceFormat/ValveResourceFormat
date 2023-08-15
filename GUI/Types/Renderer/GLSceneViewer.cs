using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Numerics;
using System.Threading;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Renderer.UniformBuffers;
using GUI.Types.ParticleRenderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;

namespace GUI.Types.Renderer
{
    internal abstract class GLSceneViewer : GLViewerControl, IGLViewer, IDisposable
    {
        public Scene Scene { get; }
        public Scene SkyboxScene { get; protected set; }
        public VrfGuiContext GuiContext => Scene.GuiContext;

        private bool ShowBaseGrid;
        public bool ShowSkybox { get; set; } = true;

        protected float SkyboxScale { get; set; } = 1.0f;
        protected Vector3 SkyboxOrigin { get; set; } = Vector3.Zero;

        public float Uptime { get; private set; }

        private bool showStaticOctree;
        private bool showDynamicOctree;
        private Frustum lockedCullFrustum;
        private Frustum skyboxLockedCullFrustum;

        protected UniformBuffer<ViewConstants> viewBuffer;
        private UniformBuffer<LightingConstants> lightingBuffer;
        private List<IBlockBindableBuffer> bufferSet;
        private bool skipRenderModeChange;
        private ComboBox renderModeComboBox;
        private InfiniteGrid baseGrid;
        private readonly Camera skyboxCamera = new();
        private OctreeDebugRenderer<SceneNode> staticOctreeRenderer;
        private OctreeDebugRenderer<SceneNode> dynamicOctreeRenderer;
        protected SelectedNodeRenderer selectedNodeRenderer;

        protected GLSceneViewer(VrfGuiContext guiContext, Frustum cullFrustum) : base()
        {
            Scene = new Scene(guiContext);
            lockedCullFrustum = cullFrustum;

            InitializeControl();

            GLLoad += OnLoad;
        }

        protected GLSceneViewer(VrfGuiContext guiContext) : base()
        {
            Scene = new Scene(guiContext);

            InitializeControl();
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
            AddCheckBox("Show Fog", Scene.FogEnabled, (v) =>
            {
                Scene.FogEnabled = v;

                if (SkyboxScene != null)
                {
                    SkyboxScene.FogEnabled = v;
                }
            });
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

            bufferSet = new List<IBlockBindableBuffer>(2) { viewBuffer, lightingBuffer };
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

            var timer = new Stopwatch();
            timer.Start();
            LoadScene();
            timer.Stop();
            Console.WriteLine($"Loading scene time: {timer.Elapsed}");

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

                // If there is no bbox, LookAt will break camera, so +1 to location.x
                bbox.Max.Z += 1f;

                var location = new Vector3(bbox.Max.Z, 0, bbox.Max.Z) * 1.5f;

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

            if (SkyboxScene != null)
            {
                skyboxCamera.Scale = SkyboxScale;
            }

            GLLoad -= OnLoad;
            GLPaint += OnPaint;

            GuiContext.ClearCache();
        }

        protected virtual void OnPaint(object sender, RenderEventArgs e)
        {
            Uptime += e.FrameTime;

            Scene.MainCamera = e.Camera;
            Scene.Update(e.FrameTime);

            selectedNodeRenderer.Update(new Scene.UpdateContext(e.FrameTime));

            // Todo: this should be set once on init, and toggled when there's F_RENDER_BACKFACES
            GL.Enable(EnableCap.CullFace);

            // For SceneSky
            viewBuffer.Data = e.Camera.SetViewConstants(viewBuffer.Data, 1.0f) with
            {
                Time = Uptime,
            };

            var genericRenderContext = new Scene.RenderContext
            {
                Camera = e.Camera,
                RenderPass = RenderPass.Both
            };

            Scene.Sky?.Render(genericRenderContext);

            if (ShowSkybox && SkyboxScene != null)
            {
                skyboxCamera.CopyFrom(e.Camera);
                skyboxCamera.SetScaledProjectionMatrix();
                skyboxCamera.SetLocation(e.Camera.Location - SkyboxOrigin);

                viewBuffer.Data = skyboxCamera.SetViewConstants(viewBuffer.Data, SkyboxScale);
                lightingBuffer.Data = SkyboxScene.LightingInfo.LightingData;

                SkyboxScene.MainCamera = skyboxCamera;
                SkyboxScene.Update(e.FrameTime);
                SkyboxScene.RenderWithCamera(skyboxCamera, bufferSet, skyboxLockedCullFrustum);

                GL.Clear(ClearBufferMask.DepthBufferBit);
            }

            viewBuffer.Data = e.Camera.SetViewConstants(viewBuffer.Data, 1.0f);
            lightingBuffer.Data = Scene.LightingInfo.LightingData;

            Scene.RenderWithCamera(e.Camera, bufferSet, lockedCullFrustum);

            selectedNodeRenderer.Render(genericRenderContext);

            if (showStaticOctree)
            {
                staticOctreeRenderer.Render(e.Camera, RenderPass.Both);
            }

            if (showDynamicOctree)
            {
                dynamicOctreeRenderer.Render(e.Camera, RenderPass.Both);
            }

            if (ShowBaseGrid)
            {
                baseGrid.Render(genericRenderContext);
            }
        }

        protected void AddBaseGridControl()
        {
            ShowBaseGrid = true;

            AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);
        }

        protected void AddRenderModeSelectionControl()
        {
#if DEBUG
            var button = new Button
            {
                Text = "Reload shaders",
                AutoSize = true,
            };

            var errorReloadingPage = new TaskDialogPage()
            {
                SizeToContent = true,
                AllowCancel = true,
                Buttons = { TaskDialogButton.OK },
                Icon = TaskDialogIcon.Error,
            };

            var reloadSemaphore = new SemaphoreSlim(1, 1);
            var reloadStopWatch = new Stopwatch();

            var lastChanged = DateTime.MinValue;
            var lastReload = DateTime.MinValue;
            var changeCoolDown = TimeSpan.FromSeconds(1);
            var reloadCoolDown = TimeSpan.FromSeconds(0.5); // There is a change that happens right after reload

            void ReloadShaders()
            {
                if (!reloadSemaphore.Wait(0))
                {
                    return;
                }

                reloadStopWatch.Restart();

                if (errorReloadingPage.BoundDialog != null)
                {
                    errorReloadingPage.Caption = "Reloading shaders…";
                }

                GuiContext.ShaderLoader.ClearCache();

                string error = null;
                try
                {
                    if (Camera.Picker is not null)
                    {
                        Camera.Picker.Dispose();
                        Camera.Picker = new PickingTexture(Scene.GuiContext, OnPicked);
                        Camera.Picker.Resize(GLControl.Width, GLControl.Height);
                    }

                    baseGrid.ReloadShader();

                    SetRenderMode(renderModeComboBox?.SelectedItem as string);
                    SetAvailableRenderModes(renderModeComboBox?.SelectedIndex ?? 0);
                }
                catch (Exception ex)
                {
                    error = ex.Message;
                    Console.Error.WriteLine(error);
                }
                finally
                {
                    lastReload = DateTime.Now;
                    reloadSemaphore.Release();
                    reloadStopWatch.Stop();
                    Console.WriteLine($"Shader reload time: {reloadStopWatch.Elapsed}, number of variants: {GuiContext.ShaderLoader.ShaderCount}");
                }

                if (error != null)
                {
                    // Hide GLControl to fix message box not showing up correctly
                    // Ref: https://stackoverflow.com/a/5080752
                    GLControl.Visible = false;

                    errorReloadingPage.Caption = "Failed to reload shaders";
                    errorReloadingPage.Text = error;

                    if (errorReloadingPage.BoundDialog == null)
                    {
                        TaskDialog.ShowDialog(this, errorReloadingPage);
                    }

                    GLControl.Visible = true;
                }
                else
                {
                    errorReloadingPage.BoundDialog?.Close();
                }
            }

            button.Click += OnButtonClick;

            void OnButtonClick(object s, EventArgs e)
            {
                ReloadShaders();
            }

            void Hotload(object s, FileSystemEventArgs e)
            {
                if (e.FullPath.EndsWith(".TMP", StringComparison.Ordinal))
                {
                    return; // Visual Studio writes to temporary file
                }

                Console.WriteLine($"[Hotload] {e.ChangeType} detected at {e.FullPath}");

                var now = DateTime.Now;
                var timeSinceLastChange = now - lastChanged;
                var timeSinceLastReload = now - lastReload;

                if (!Visible || reloadSemaphore.CurrentCount == 0
                    || timeSinceLastReload < reloadCoolDown
                    || timeSinceLastChange < changeCoolDown)
                {
                    return;
                }

                lastChanged = now;

                ReloadShaders();
            };

            void OnDisposedLocal(object e, EventArgs a)
            {
                Disposed -= OnDisposedLocal;
                button.Click -= OnButtonClick;
                GuiContext.ShaderLoader.ShaderWatcher.SynchronizingObject = null;
                GuiContext.ShaderLoader.ShaderWatcher.Changed -= Hotload;
                GuiContext.ShaderLoader.ShaderWatcher.Created -= Hotload;
                GuiContext.ShaderLoader.ShaderWatcher.Renamed -= Hotload;
                reloadSemaphore.Dispose();
            }

            GuiContext.ShaderLoader.ShaderWatcher.SynchronizingObject = this;
            GuiContext.ShaderLoader.ShaderWatcher.Changed += Hotload;
            GuiContext.ShaderLoader.ShaderWatcher.Created += Hotload;
            GuiContext.ShaderLoader.ShaderWatcher.Renamed += Hotload;
            Disposed += OnDisposedLocal;

            AddControl(button);
#endif

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
    }
}

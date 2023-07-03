using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.ParticleRenderer;
using GUI.Utils;
using OpenTK.Graphics.OpenGL;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    internal abstract class GLSceneViewer : IGLViewer
    {
        public Scene Scene { get; }
        public Scene SkyboxScene { get; protected set; }
        public GLViewerControl ViewerControl { get; }
        public VrfGuiContext GuiContext => Scene.GuiContext;

        public bool ShowBaseGrid { get; set; } = true;
        public bool ShowSkybox { get; set; } = true;

        protected float SkyboxScale { get; set; } = 1.0f;
        protected Vector3 SkyboxOrigin { get; set; } = Vector3.Zero;

        private bool showStaticOctree;
        private bool showDynamicOctree;
        private Frustum lockedCullFrustum;

        private bool skipRenderModeChange;
        private ComboBox renderModeComboBox;
        private ParticleGrid baseGrid;
        private readonly Camera skyboxCamera = new();
        private OctreeDebugRenderer<SceneNode> staticOctreeRenderer;
        private OctreeDebugRenderer<SceneNode> dynamicOctreeRenderer;
        protected SelectedNodeRenderer selectedNodeRenderer;

        protected GLSceneViewer(VrfGuiContext guiContext, Frustum cullFrustum)
        {
            Scene = new Scene(guiContext);
            ViewerControl = new GLViewerControl(this);
            lockedCullFrustum = cullFrustum;

            InitializeControl();

            ViewerControl.AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);

            ViewerControl.GLLoad += OnLoad;
        }

        protected GLSceneViewer(VrfGuiContext guiContext)
        {
            Scene = new Scene(guiContext);
            ViewerControl = new GLViewerControl(this);

            InitializeControl();
            ViewerControl.AddCheckBox("Show Static Octree", showStaticOctree, (v) =>
            {
                showStaticOctree = v;

                if (showStaticOctree)
                {
                    staticOctreeRenderer.StaticBuild();
                }
            });
            ViewerControl.AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);
            ViewerControl.AddCheckBox("Show Tool Materials", Scene.ShowToolsMaterials, (v) =>
            {
                Scene.ShowToolsMaterials = v;

                if (SkyboxScene != null)
                {
                    SkyboxScene.ShowToolsMaterials = v;
                }
            });
            ViewerControl.AddCheckBox("Lock Cull Frustum", false, (v) =>
            {
                if (v)
                {
                    lockedCullFrustum = Scene.MainCamera.ViewFrustum.Clone();
                }
                else
                {
                    lockedCullFrustum = null;
                }
            });

            ViewerControl.GLLoad += OnLoad;
        }

        protected abstract void InitializeControl();

        protected abstract void LoadScene();

        protected abstract void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo);

        private void OnLoad(object sender, EventArgs e)
        {
            baseGrid = new ParticleGrid(20, 5, GuiContext);
            selectedNodeRenderer = new(GuiContext);

            ViewerControl.Camera.SetViewportSize(ViewerControl.GLControl.Width, ViewerControl.GLControl.Height);

            ViewerControl.Camera.Picker = new PickingTexture(Scene.GuiContext, OnPicked);

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
                var location = new Vector3(bbox.Max.Z + 1, 0, bbox.Max.Z) * 1.5f;

                ViewerControl.Camera.SetLocation(location);
                ViewerControl.Camera.LookAt(bbox.Center);
            }
            else
            {
                ViewerControl.Camera.SetLocation(new Vector3(256));
                ViewerControl.Camera.LookAt(new Vector3(0));
            }

            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
            dynamicOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.DynamicOctree, Scene.GuiContext, true);

            SetAvailableRenderModes();

            if (ShowSkybox && SkyboxScene != null)
            {
                skyboxCamera.Scale = SkyboxScale;
            }

            ViewerControl.GLLoad -= OnLoad;
            ViewerControl.GLPaint += OnPaint;

            GuiContext.ClearCache();
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            Scene.MainCamera = e.Camera;
            Scene.Update(e.FrameTime);

            GL.Enable(EnableCap.CullFace);

            Scene.Sky?.Render(new Scene.RenderContext { Camera = e.Camera });

            if (ShowBaseGrid)
            {
                baseGrid.Render(e.Camera, RenderPass.Both);
            }

            if (ShowSkybox && SkyboxScene != null)
            {
                skyboxCamera.CopyFrom(e.Camera);
                skyboxCamera.SetScaledProjectionMatrix();
                skyboxCamera.SetLocation(e.Camera.Location - SkyboxOrigin);

                SkyboxScene.MainCamera = skyboxCamera;
                SkyboxScene.Update(e.FrameTime);
                SkyboxScene.RenderWithCamera(skyboxCamera);

                GL.Clear(ClearBufferMask.DepthBufferBit);
            }

            Scene.RenderWithCamera(e.Camera, lockedCullFrustum);

            selectedNodeRenderer.Render(e.Camera, RenderPass.Both);

            if (showStaticOctree)
            {
                staticOctreeRenderer.Render(e.Camera, RenderPass.Both);
            }

            if (showDynamicOctree)
            {
                dynamicOctreeRenderer.Render(e.Camera, RenderPass.Both);
            }
        }

        protected void AddRenderModeSelectionControl()
        {
#if DEBUG
            var button = new Button
            {
                Text = "Reload shaders",
                AutoSize = true,
            };
            button.Click += (s, e) =>
            {
                if (ViewerControl.Camera.Picker is not null)
                {
                    ViewerControl.Camera.Picker.Dispose();
                    ViewerControl.Camera.Picker = new PickingTexture(Scene.GuiContext, OnPicked);
                    ViewerControl.Camera.Picker.Resize(ViewerControl.GLControl.Width, ViewerControl.GLControl.Height);
                }

                GuiContext.ShaderLoader.ClearCache();

                try
                {
                    SetRenderMode(renderModeComboBox?.SelectedItem as string);
                    SetAvailableRenderModes(renderModeComboBox?.SelectedIndex ?? 0);
                }
                catch (Exception ex)
                {
                    Console.Error.WriteLine(ex.Message);

                    MessageBox.Show(
                        $"{ex.Message}",
                        "Failed to reload shaders",
                        MessageBoxButtons.OK,
                        MessageBoxIcon.Error
                    );
                }
            };
            ViewerControl.AddControl(button);
#endif

            renderModeComboBox ??= ViewerControl.AddSelection("Render Mode", (renderMode, _) =>
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
                    .Distinct()
                    .Concat(ViewerControl.Camera.Picker.Shader.RenderModes);

                renderModeComboBox.BeginUpdate();
                renderModeComboBox.Items.Clear();
                renderModeComboBox.Enabled = true;
                renderModeComboBox.Items.AddRange(supportedRenderModes.Prepend("Default Render Mode").ToArray());
                skipRenderModeChange = true;
                renderModeComboBox.SelectedIndex = index;
                renderModeComboBox.EndUpdate();
            }
        }

        protected void SetEnabledLayers(HashSet<string> layers)
        {
            Scene.SetEnabledLayers(layers);
            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
        }

        private void SetRenderMode(string renderMode)
        {
            ViewerControl.Camera?.Picker.SetRenderMode(renderMode);
            Scene.Sky?.SetRenderMode(renderMode);

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
    }
}

using System;
using System.Collections.Generic;
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
    internal abstract class GLSceneViewer
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

        private ComboBox renderModeComboBox;
        private ParticleGrid baseGrid;
        private Camera skyboxCamera = new Camera();
        private OctreeDebugRenderer<SceneNode> staticOctreeRenderer;
        private OctreeDebugRenderer<SceneNode> dynamicOctreeRenderer;

        protected GLSceneViewer(VrfGuiContext guiContext, Frustum cullFrustum)
        {
            Scene = new Scene(guiContext);
            ViewerControl = new GLViewerControl();
            lockedCullFrustum = cullFrustum;

            InitializeControl();

            ViewerControl.AddCheckBox("Show Grid", ShowBaseGrid, (v) => ShowBaseGrid = v);

            ViewerControl.GLLoad += OnLoad;
        }

        protected GLSceneViewer(VrfGuiContext guiContext)
        {
            Scene = new Scene(guiContext);
            ViewerControl = new GLViewerControl();

            InitializeControl();
            ViewerControl.AddCheckBox("Show Static Octree", showStaticOctree, (v) => showStaticOctree = v);
            ViewerControl.AddCheckBox("Show Dynamic Octree", showDynamicOctree, (v) => showDynamicOctree = v);
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

        private void OnLoad(object sender, EventArgs e)
        {
            baseGrid = new ParticleGrid(20, 5, GuiContext);

            ViewerControl.Camera.SetViewportSize(ViewerControl.GLControl.Width, ViewerControl.GLControl.Height);
            ViewerControl.Camera.SetLocation(new Vector3(256));
            ViewerControl.Camera.LookAt(new Vector3(0));

            LoadScene();

            if (Scene.AllNodes.Any())
            {
                var bbox = Scene.AllNodes.First().BoundingBox;
                //if first node has no bbox, LookAt will break camera, so +1 to location.x
                var location = new Vector3(bbox.Max.Z + 1, 0, bbox.Max.Z) * 1.5f;

                ViewerControl.Camera.SetLocation(location);
                ViewerControl.Camera.LookAt(bbox.Center);
            }

            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
            dynamicOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.DynamicOctree, Scene.GuiContext, true);

            if (renderModeComboBox != null)
            {
                var supportedRenderModes = Scene.AllNodes
                    .SelectMany(r => r.GetSupportedRenderModes())
                    .Distinct();
                SetAvailableRenderModes(supportedRenderModes);
            }

            ViewerControl.GLLoad -= OnLoad;
            ViewerControl.GLPaint += OnPaint;

            GuiContext.ClearCache();
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            Scene.MainCamera = e.Camera;
            Scene.Update(e.FrameTime);

            if (ShowBaseGrid)
            {
                baseGrid.Render(e.Camera, RenderPass.Both);
            }

            if (ShowSkybox && SkyboxScene != null)
            {
                skyboxCamera.CopyFrom(e.Camera);
                skyboxCamera.SetLocation(e.Camera.Location - SkyboxOrigin);
                skyboxCamera.SetScale(SkyboxScale);

                SkyboxScene.MainCamera = skyboxCamera;
                SkyboxScene.Update(e.FrameTime);
                SkyboxScene.RenderWithCamera(skyboxCamera);

                GL.Clear(ClearBufferMask.DepthBufferBit);
            }

            Scene.RenderWithCamera(e.Camera, lockedCullFrustum);

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
            if (renderModeComboBox == null)
            {
                renderModeComboBox = ViewerControl.AddSelection("Render Mode", (renderMode, _) =>
                {
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
                });
            }
        }

        private void SetAvailableRenderModes(IEnumerable<string> renderModes)
        {
            renderModeComboBox.Items.Clear();
            if (renderModes.Any())
            {
                renderModeComboBox.Enabled = true;
                renderModeComboBox.Items.Add("Default Render Mode");
                renderModeComboBox.Items.AddRange(renderModes.ToArray());
                renderModeComboBox.SelectedIndex = 0;
            }
            else
            {
                renderModeComboBox.Items.Add("(no render modes available)");
                renderModeComboBox.SelectedIndex = 0;
                renderModeComboBox.Enabled = false;
            }
        }

        protected void SetEnabledLayers(HashSet<string> layers)
        {
            Scene.SetEnabledLayers(layers);
            staticOctreeRenderer = new OctreeDebugRenderer<SceneNode>(Scene.StaticOctree, Scene.GuiContext, false);
        }
    }
}

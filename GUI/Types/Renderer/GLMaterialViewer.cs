using System;
using System.Numerics;
using System.Windows.Forms;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    class GLMaterialViewer : GLSceneViewer
    {
        private readonly ValveResourceFormat.Resource Resource;
        private readonly TabControl Tabs;
        private MaterialRenderer Renderer;

        public GLMaterialViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource, TabControl tabs) : base(guiContext, Frustum.CreateEmpty())
        {
            Resource = resource;
            Tabs = tabs;
            ShowBaseGrid = false;
        }

        protected override void LoadScene()
        {
            Renderer = new MaterialRenderer(Scene, Resource);
            Scene.Add(Renderer, false);

            viewBuffer.Data = viewBuffer.Data with
            {
                ViewToProjection = Matrix4x4.Identity,
                WorldToProjection = Matrix4x4.Identity,
                WorldToView = Matrix4x4.Identity,
                CameraPosition = Vector3.Zero,
            };
        }

        protected override void OnPaint(object sender, RenderEventArgs e)
        {
            Renderer.Render(new Scene.RenderContext());
        }

        private void OnShadersButtonClick(object s, EventArgs e)
        {
            var material = (Material)Resource.DataBlock;

            var shaders = GuiContext.FileLoader.LoadShader(material.ShaderName);

            var featureState = ShaderDataProvider.GetMaterialFeatureState(material);

            AddZframeTab(shaders.Vertex);
            AddZframeTab(shaders.Pixel);

            void AddZframeTab(ValveResourceFormat.CompiledShader.ShaderFile stage)
            {
                var result = ShaderDataProvider.GetStaticConfiguration_ForFeatureState(shaders.Features, stage, featureState);

                var zframeTab = new TabPage($"{stage.VcsProgramType} Static[{result.ZFrameId}]");
                var zframeRichTextBox = new CompiledShader.ZFrameRichTextBox(Tabs, stage, shaders, result.ZFrameId);
                zframeTab.Controls.Add(zframeRichTextBox);

                using var zFrame = stage.GetZFrameFile(result.ZFrameId);
                var gpuSourceTab = CompiledShader.CreateDecompiledTabPage(shaders, stage, zFrame, 0, $"{stage.VcsProgramType} Source[0]");

                Tabs.Controls.Add(zframeTab);
                Tabs.TabPages.Add(gpuSourceTab);
                Tabs.SelectTab(gpuSourceTab);
            }
        }

        private void AddShaderButton()
        {
            var button = new Button
            {
                Text = "Open shader zframe",
                AutoSize = true,
            };

            button.Click += OnShadersButtonClick;

            AddControl(button);
        }

        protected override void InitializeControl()
        {
            //AddRenderModeSelectionControl();
            AddShaderButton();
        }

        protected override void OnPicked(object sender, PickingTexture.PickingResponse pixelInfo)
        {
            //
        }
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Viewers;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO.ShaderDataProvider;
using ValveResourceFormat.ResourceTypes;
using static System.Windows.Forms.TabControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    class GLMaterialViewer : GLViewerControl, IGLViewer
    {
        private ICollection<MaterialRenderer> Renderers { get; } = new HashSet<MaterialRenderer>();
        private readonly VrfGuiContext GuiContext;
        private readonly ValveResourceFormat.Resource Resource;
        private readonly TabControl Tabs;

        public GLMaterialViewer(VrfGuiContext guiContext, ValveResourceFormat.Resource resource, TabControl tabs) : base()
        {
            GuiContext = guiContext;
            Resource = resource;
            Tabs = tabs;

            AddShaderButton();

            GLLoad += OnLoad;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                GLPaint -= OnPaint;
            }

            base.Dispose(disposing);
        }

        private void OnLoad(object sender, EventArgs e)
        {
            GLLoad -= OnLoad;
            GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            foreach (var renderer in Renderers)
            {
                renderer.Render(e.Camera, RenderPass.Both);
            }
        }

        public void AddRenderer(MaterialRenderer renderer)
        {
            Renderers.Add(renderer);
        }

        private void OnShadersButtonClick(object s, EventArgs e)
        {
            var material = (Material)Resource.DataBlock;

            var shaders = GuiContext.FileLoader.LoadShader(material.ShaderName);

            var featureState = FullShaderDataProvider.GetMaterialFeatureState(material);

            AddZframeTab(shaders.Vertex);
            AddZframeTab(shaders.Pixel);

            void AddZframeTab(ValveResourceFormat.CompiledShader.ShaderFile stage)
            {
                var result = FullShaderDataProvider.GetStaticConfiguration_ForFeatureState(shaders.Features, stage, featureState);

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
    }
}

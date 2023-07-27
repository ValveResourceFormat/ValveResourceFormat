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
            var shaderData = new FullShaderDataProvider(GuiContext.FileLoader, false);

            var material = (Material)Resource.DataBlock;

            var textureKey = material.TextureParams.First().Key; // TODO: Why are we looking up a specific texture?

            var test = shaderData.GetZFrame_TEST_DO_NOT_MERGE(textureKey, material);

            var zframeTab = new TabPage("ZFrame");
            var zframeRichTextBox = new CompiledShader.ZFrameRichTextBox(Tabs, test.Shader, test.Collection, test.ZFrame.ZframeId);
            zframeTab.Controls.Add(zframeRichTextBox);

            var gpuSourceTab = CompiledShader.CreateDecompiledTabPage(test.Collection, test.Shader, test.ZFrame, 0, "Shader");

            Tabs.TabPages.Add(gpuSourceTab);
            Tabs.Controls.Add(zframeTab);
            Tabs.SelectTab(gpuSourceTab);
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

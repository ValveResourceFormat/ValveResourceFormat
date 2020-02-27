using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with world controls (render mode, camera selection).
    /// Renders a list of IMeshRenderers.
    /// </summary>
    internal class GLWorldViewer
    {
        private ICollection<IRenderer> Renderers { get; } = new HashSet<IRenderer>();

        public event EventHandler Load;

        public Control Control => viewerControl;

        private readonly GLViewerControl viewerControl;

        private ComboBox renderModeComboBox;

        public GLWorldViewer()
        {
            viewerControl = new GLViewerControl();

            InitializeControl();

            viewerControl.GLLoad += OnLoad;
        }

        private void InitializeControl()
        {
            renderModeComboBox = viewerControl.AddSelection("Render Mode", (renderMode, _) =>
            {
                foreach (var renderer in Renderers.OfType<IMeshRenderer>())
                {
                    renderer.SetRenderMode(renderMode);
                }
            });
        }

        private void OnLoad(object sender, EventArgs e)
        {
            viewerControl.Camera.SetViewportSize(viewerControl.GLControl.Width, viewerControl.GLControl.Height);
            viewerControl.Camera.SetLocation(new Vector3(200));
            viewerControl.Camera.LookAt(new Vector3(0));

            Load?.Invoke(this, e);

            viewerControl.GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            foreach (var renderer in Renderers)
            {
                renderer.Update(e.FrameTime);
                renderer.Render(e.Camera, RenderPass.Both);
            }
        }

        public void AddRenderer(IRenderer renderer)
        {
            Renderers.Add(renderer);

            if (renderer is IMeshRenderer)
            {
                // Update supported render modes
                var supportedRenderModes = Renderers
                    .OfType<IMeshRenderer>()
                    .SelectMany(r => r.GetSupportedRenderModes())
                    .Distinct();

                SetRenderModes(supportedRenderModes);
            }
        }

        private void SetRenderModes(IEnumerable<string> renderModes)
        {
            renderModeComboBox.Items.Clear();
            if (renderModes.Any())
            {
                renderModeComboBox.Enabled = true;
                renderModeComboBox.Items.Add("Default render mode");
                renderModeComboBox.Items.AddRange(renderModes.ToArray());
                renderModeComboBox.SelectedIndex = 0;
            }
            else
            {
                renderModeComboBox.Items.Add("No render modes available");
                renderModeComboBox.SelectedIndex = 0;
                renderModeComboBox.Enabled = false;
            }
        }
    }
}

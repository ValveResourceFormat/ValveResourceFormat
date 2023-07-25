using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.ParticleRenderer;
using GUI.Utils;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with particle controls (control points? particle counts?).
    /// Renders a list of ParticleRenderers.
    /// </summary>
    class GLParticleViewer : GLViewerControl, IGLViewer
    {
        private ICollection<ParticleRenderer.ParticleRenderer> Renderers { get; } = new HashSet<ParticleRenderer.ParticleRenderer>();

        private ComboBox renderModeComboBox;
        private readonly VrfGuiContext vrfGuiContext;

        private ParticleGrid particleGrid;

        public GLParticleViewer(VrfGuiContext guiContext) : base()
        {
            vrfGuiContext = guiContext;

            renderModeComboBox = AddSelection("Render Mode", (renderMode, _) => SetRenderMode(renderMode));

            GLLoad += OnLoad;
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                renderModeComboBox?.Dispose();
            }

            base.Dispose(disposing);
        }

        private void OnLoad(object sender, EventArgs e)
        {
            particleGrid = new ParticleGrid(20, 5, vrfGuiContext);

            Camera.SetViewportSize(GLControl.Width, GLControl.Height);
            Camera.SetLocation(new Vector3(200));
            Camera.LookAt(new Vector3(0));

            var supportedRenderModes = Renderers
                    .SelectMany(r => r.GetSupportedRenderModes())
                    .Distinct();
            SetAvailableRenderModes(supportedRenderModes);

            GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            particleGrid.Render(e.Camera, RenderPass.Both);

            foreach (var renderer in Renderers)
            {
                renderer.Update(e.FrameTime);
                // If particle is finished, restart it
                if (renderer.IsFinished())
                {
                    renderer.Restart();
                }

                renderer.Render(e.Camera, RenderPass.Both);
            }
        }

        public void AddRenderer(ParticleRenderer.ParticleRenderer renderer)
        {
            Renderers.Add(renderer);
        }

        private void SetAvailableRenderModes(IEnumerable<string> renderModes)
        {
            renderModeComboBox.Items.Clear();
            renderModeComboBox.Enabled = true;
            renderModeComboBox.Items.Add("Default Render Mode");
            renderModeComboBox.Items.AddRange(renderModes.ToArray());
            renderModeComboBox.SelectedIndex = 0;
        }

        private void SetRenderMode(string renderMode)
        {
            foreach (var renderer in Renderers)
            {
                renderer.SetRenderMode(renderMode);
            }
        }
    }
}

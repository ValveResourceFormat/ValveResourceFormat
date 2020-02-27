using System;
using System.Collections.Generic;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.ParticleRenderer;
using GUI.Utils;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with particle controls (control points? particle counts?).
    /// Renders a list of ParticleRenderers.
    /// </summary>
    internal class GLParticleViewer
    {
        private ICollection<ParticleRenderer.ParticleRenderer> Renderers { get; } = new HashSet<ParticleRenderer.ParticleRenderer>();

        public event EventHandler Load;

        public Control Control => viewerControl;

        private readonly GLViewerControl viewerControl;
        private readonly VrfGuiContext vrfGuiContext;

        private ParticleGrid particleGrid;

        public GLParticleViewer(VrfGuiContext guiContext)
        {
            vrfGuiContext = guiContext;

            viewerControl = new GLViewerControl();

            InitializeControl();

            viewerControl.GLLoad += OnLoad;
        }

        private void InitializeControl()
        {
        }

        private void OnLoad(object sender, EventArgs e)
        {
            particleGrid = new ParticleGrid(20, 5, vrfGuiContext);

            viewerControl.Camera.SetViewportSize(viewerControl.GLControl.Width, viewerControl.GLControl.Height);
            viewerControl.Camera.SetLocation(new Vector3(200));
            viewerControl.Camera.LookAt(new Vector3(0));

            Load?.Invoke(this, e);

            viewerControl.GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            particleGrid.Render(e.Camera, RenderPass.Both);

            foreach (var renderer in Renderers)
            {
                renderer.Update(e.FrameTime);

                renderer.Render(e.Camera, RenderPass.Both);
            }
        }

        public void AddRenderer(ParticleRenderer.ParticleRenderer renderer)
        {
            Renderers.Add(renderer);
        }
    }
}

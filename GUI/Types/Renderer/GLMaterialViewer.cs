using System;
using System.Collections.Generic;
using System.Windows.Forms;
using GUI.Controls;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    internal class GLMaterialViewer
    {
        private ICollection<MaterialRenderer> Renderers { get; } = new HashSet<MaterialRenderer>();

        public event EventHandler Load;

        public Control Control => viewerControl;

        private readonly GLViewerControl viewerControl;

        public GLMaterialViewer()
        {
            viewerControl = new GLViewerControl();

            InitializeControl();

            viewerControl.GLLoad += OnLoad;
        }

        private void InitializeControl()
        {
        }

        private void OnLoad(object sender, EventArgs e)
        {
            Load?.Invoke(this, e);

            viewerControl.GLPaint += OnPaint;
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
    }
}

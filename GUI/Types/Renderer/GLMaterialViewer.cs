using System;
using System.Collections.Generic;
using GUI.Controls;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with material controls (render modes maybe at some point?).
    /// Renders a list of MatarialRenderers.
    /// </summary>
    class GLMaterialViewer : GLViewerControl, IGLViewer
    {
        private ICollection<MaterialRenderer> Renderers { get; } = new HashSet<MaterialRenderer>();

        public GLMaterialViewer() : base()
        {
            GLLoad += OnLoad;
        }

        private void OnLoad(object sender, EventArgs e)
        {
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
    }
}

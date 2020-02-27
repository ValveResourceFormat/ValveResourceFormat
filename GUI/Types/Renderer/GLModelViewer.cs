using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.ParticleRenderer;
using GUI.Utils;
using static GUI.Controls.GLViewerControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// Renders a list of IMeshRenderers.
    /// </summary>
    internal class GLModelViewer
    {
        private ICollection<IRenderer> Renderers { get; } = new HashSet<IRenderer>();

        public event EventHandler Load;

        public Control Control => viewerControl;

        private readonly GLViewerControl viewerControl;
        private readonly VrfGuiContext vrfGuiContext;

        private ParticleGrid grid;

        private Label drawCallsLabel;
        private ComboBox animationComboBox;
        private ComboBox renderModeComboBox;

        public GLModelViewer(VrfGuiContext guiContext)
        {
            vrfGuiContext = guiContext;

            viewerControl = new GLViewerControl();

            InitializeControl();

            viewerControl.GLLoad += OnLoad;
        }

        private void InitializeControl()
        {
            drawCallsLabel = viewerControl.AddLabel("Drawcalls: 0");

            renderModeComboBox = viewerControl.AddSelection("Render Mode", (renderMode, _) =>
            {
                foreach (var renderer in Renderers.OfType<IMeshRenderer>())
                {
                    renderer.SetRenderMode(renderMode);
                }
            });

            animationComboBox = viewerControl.AddSelection("Animation", (animation, _) =>
            {
                foreach (var renderer in Renderers.OfType<IAnimationRenderer>())
                {
                    renderer.SetAnimation(animation);
                }
            });
        }

        private void OnLoad(object sender, EventArgs e)
        {
            grid = new ParticleGrid(20, 5, vrfGuiContext);

            viewerControl.Camera.SetViewportSize(viewerControl.GLControl.Width, viewerControl.GLControl.Height);
            viewerControl.Camera.SetLocation(new Vector3(200));
            viewerControl.Camera.LookAt(new Vector3(0));

            Load?.Invoke(this, e);

            viewerControl.GLPaint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            grid.Render(e.Camera, RenderPass.Both);

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

                // Update supported animations
                var supportedAnimations = Renderers
                    .OfType<IAnimationRenderer>()
                    .SelectMany(r => r.GetSupportedAnimationNames())
                    .Distinct();

                SetAnimations(supportedAnimations);
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

        private void SetAnimations(IEnumerable<string> animations)
        {
            animationComboBox.Items.Clear();
            if (animations.Any())
            {
                animationComboBox.Enabled = true;
                animationComboBox.Items.AddRange(animations.ToArray());
                animationComboBox.SelectedIndex = 0;
            }
            else
            {
                animationComboBox.Items.Add("No animations available");
                animationComboBox.SelectedIndex = 0;
                animationComboBox.Enabled = false;
            }
        }
    }
}

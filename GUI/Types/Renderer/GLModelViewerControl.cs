using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;
using OpenTK;
using static GUI.Types.GLRenderControl;

namespace GUI.Types.Renderer
{
    /// <summary>
    /// GL Render control with model controls (render mode, animation panels).
    /// Renders a list of IMeshRenderers.
    /// </summary>
    internal class GLModelViewerControl
    {
        private ICollection<IRenderer> Renderers { get; } = new HashSet<IRenderer>();

        public event EventHandler Load;

        public Control Control => glRenderControl.Control;

        private readonly GLRenderControl glRenderControl;

        private ComboBox animationComboBox;
        private ComboBox renderModeComboBox;

        public GLModelViewerControl()
        {
            glRenderControl = new GLRenderControl();

            InitializeControl();

            glRenderControl.Load += OnLoad;
        }

        public void Unload()
        {
            glRenderControl.Paint -= OnPaint;
        }

        private void OnLoad(object sender, EventArgs e)
        {
            glRenderControl.Load -= OnLoad;

            glRenderControl.Camera.SetViewportSize(glRenderControl.Control.Width, glRenderControl.Control.Height);
            glRenderControl.Camera.SetLocation(new Vector3(200));
            glRenderControl.Camera.LookAt(new Vector3(0));

            Load?.Invoke(this, e);

            glRenderControl.Paint += OnPaint;
        }

        private void OnPaint(object sender, RenderEventArgs e)
        {
            foreach (var renderer in Renderers)
            {
                renderer.Update(e.FrameTime);
                renderer.Render(e.Camera);
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
            renderModeComboBox.Items.Add("Change render mode...");
            renderModeComboBox.Items.AddRange(renderModes.ToArray());
            renderModeComboBox.SelectedIndex = 0;
        }

        private void SetAnimations(IEnumerable<string> animations)
        {
            animationComboBox.Items.Clear();
            if (animations.Any())
            {
                animationComboBox.Items.AddRange(animations.ToArray());
                animationComboBox.SelectedIndex = 0;
            }
        }

        private void InitializeControl()
        {
            var control = glRenderControl.Control;

            // Add combobox for render modes
            renderModeComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            renderModeComboBox.SelectedIndexChanged += OnRenderModeChange;
            control.Controls.Add(renderModeComboBox);

            // Add combobox for animations
            animationComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            animationComboBox.SelectedIndexChanged += OnAnimationChange;
            control.Controls.Add(animationComboBox);
        }

        private void OnRenderModeChange(object obj, EventArgs e)
        {
            if (renderModeComboBox.SelectedIndex > 0)
            {
                renderModeComboBox.Items[0] = "Default";
            }

            var selectedRenderMode = renderModeComboBox.SelectedIndex == 0
                ? null
                : renderModeComboBox.SelectedItem.ToString();

            foreach (var renderer in Renderers)
            {
                if (renderer is IMeshRenderer meshRenderer)
                {
                    meshRenderer.SetRenderMode(selectedRenderMode);
                }
            }
        }

        private void OnAnimationChange(object obj, EventArgs e)
        {
            foreach (var renderer in Renderers.OfType<IAnimationRenderer>())
            {
                renderer.SetAnimation(animationComboBox.SelectedItem.ToString());
            }
        }
    }
}

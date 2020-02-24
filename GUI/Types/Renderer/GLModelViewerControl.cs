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

        private CheckedListBox animationComboBox;
        private CheckedListBox renderModeComboBox;

        public GLModelViewerControl()
        {
            glRenderControl = new GLRenderControl();

            InitializeControl();

            glRenderControl.Load += OnLoad;
        }

        private void OnLoad(object sender, EventArgs e)
        {
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
                renderer.Render(e.Camera, RenderPass.None);
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
            renderModeComboBox.Items.Add("Default render mode");
            renderModeComboBox.Items.AddRange(renderModes.ToArray());
            renderModeComboBox.SetItemChecked(0, true);
        }

        private void SetAnimations(IEnumerable<string> animations)
        {
            animationComboBox.Items.Clear();
            if (animations.Any())
            {
                animationComboBox.Items.AddRange(animations.ToArray());
                renderModeComboBox.SetItemChecked(0, true);
            }
        }

        private void InitializeControl()
        {
            var control = glRenderControl.Control.ViewerControls;

            // Add combobox for render modes
            renderModeComboBox = new CheckedListBox
            {
                Dock = DockStyle.Top,
                CheckOnClick = true,
            };

            renderModeComboBox.ItemCheck += OnRenderModeChange;
            control.Controls.Add(renderModeComboBox);

            // Add combobox for animations
            animationComboBox = new CheckedListBox
            {
                Dock = DockStyle.Top,
                CheckOnClick = true,
            };

            animationComboBox.ItemCheck += OnAnimationChange;
            control.Controls.Add(animationComboBox);
        }

        private void OnRenderModeChange(object obj, ItemCheckEventArgs e)
        {
            if (e.NewValue != CheckState.Checked)
            {
                return;
            }

            if (renderModeComboBox.CheckedItems.Count > 0)
            {
                renderModeComboBox.ItemCheck -= OnRenderModeChange;
                renderModeComboBox.SetItemChecked(renderModeComboBox.CheckedIndices[0], false);
                renderModeComboBox.ItemCheck += OnRenderModeChange;
            }

            var selectedRenderMode = e.Index == 0 ? null : renderModeComboBox.Items[e.Index].ToString();

            foreach (var renderer in Renderers.OfType<IMeshRenderer>())
            {
                renderer.SetRenderMode(selectedRenderMode);
            }
        }

        private void OnAnimationChange(object obj, ItemCheckEventArgs e)
        {
            if (e.NewValue != CheckState.Checked)
            {
                return;
            }

            if (animationComboBox.CheckedItems.Count > 0)
            {
                animationComboBox.ItemCheck -= OnRenderModeChange;
                animationComboBox.SetItemChecked(animationComboBox.CheckedIndices[0], false);
                animationComboBox.ItemCheck += OnRenderModeChange;
            }

            var selectedAnimation = animationComboBox.Items[e.Index].ToString();

            foreach (var renderer in Renderers.OfType<IAnimationRenderer>())
            {
                renderer.SetAnimation(selectedAnimation);
            }
        }
    }
}

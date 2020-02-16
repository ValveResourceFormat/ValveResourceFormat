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
    internal class GLModelRenderControl
    {
        public ICollection<IMeshRenderer> Renderers { get; } = new HashSet<IMeshRenderer>();

        public event EventHandler Load;

        public Control Control => glRenderControl.Control;

        private readonly GLRenderControl glRenderControl;

        private ComboBox renderModeComboBox;

        public GLModelRenderControl()
        {
            glRenderControl = new GLRenderControl();

            InitializeControl();

            glRenderControl.Load += OnLoad;
        }

        /// <summary>
        /// OpenGL loaded event handler.
        /// </summary>
        public void OnLoad(object sender, EventArgs e)
        {
            glRenderControl.Camera.SetViewportSize(glRenderControl.Control.Width, glRenderControl.Control.Height);
            glRenderControl.Camera.SetLocation(new Vector3(200));
            glRenderControl.Camera.LookAt(new Vector3(0));

            Load?.Invoke(this, e);

            glRenderControl.Paint += OnPaint;
        }

        /// <summary>
        /// Render control event.
        /// </summary>
        public void OnPaint(object sender, RenderEventArgs e)
        {
            foreach (var renderer in Renderers)
            {
                renderer.Update(e.FrameTime);
                renderer.Render(e.Camera);
            }
        }

        public void AddRenderer(IMeshRenderer renderer)
        {
            Renderers.Add(renderer);

            // Update supported render modes
            var supportedRenderModes = Renderers
                .SelectMany(r => r.GetSupportedRenderModes())
                .Distinct();

            SetRenderModes(supportedRenderModes);
        }

        private void SetRenderModes(IEnumerable<string> renderModes)
        {
            renderModeComboBox.Items.Clear();
            renderModeComboBox.Items.Add("Change render mode...");
            renderModeComboBox.Items.AddRange(renderModes.ToArray());
            renderModeComboBox.SelectedIndex = 0;
        }

        private void InitializeControl()
        {
            var control = glRenderControl.Control;

            renderModeComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            renderModeComboBox.SelectedIndexChanged += OnRenderModeChange;
            control.Controls.Add(renderModeComboBox);
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
                renderer.SetRenderMode(selectedRenderMode);
            }
        }
    }
}

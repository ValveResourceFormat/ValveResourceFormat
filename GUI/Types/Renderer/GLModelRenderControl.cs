using System;
using System.Collections.Generic;
using System.Linq;
using System.Windows.Forms;

namespace GUI.Types.Renderer
{
    internal class GLModelRenderControl : GLRenderControl
    {
        public event EventHandler<string> OnRenderModeChanged;

        private ComboBox renderModeComboBox;

        public void SetRenderModes(IEnumerable<string> renderModes)
        {
            renderModeComboBox.Items.Clear();
            renderModeComboBox.Items.Add("Change render mode...");
            renderModeComboBox.Items.AddRange(renderModes.ToArray());
            renderModeComboBox.SelectedIndex = 0;
        }

        protected override Control InitializeControl()
        {
            var control = base.InitializeControl();

            renderModeComboBox = new ComboBox
            {
                Dock = DockStyle.Top,
                DropDownStyle = ComboBoxStyle.DropDownList,
            };

            renderModeComboBox.SelectedIndexChanged += OnRenderModeChange;
            control.Controls.Add(renderModeComboBox);

            return control;
        }

        private void OnRenderModeChange(object obj, EventArgs e)
        {
            if (renderModeComboBox.SelectedIndex > 0)
            {
                renderModeComboBox.Items[0] = "Default";
            }

            OnRenderModeChanged?.Invoke(obj, renderModeComboBox.SelectedItem.ToString());
        }
    }
}

using System.Drawing;
using System.Windows.Forms;

namespace GUI.Controls
{
    partial class GLViewerSelectionControl : UserControl
    {
        public ComboBox ComboBox => comboBox;

        private GLViewerSelectionControl()
        {
            InitializeComponent();
        }


        // todo: fix behavior with fill = false
        public GLViewerSelectionControl(string name, bool horizontal, bool fill)
            : this()
        {
            SuspendLayout();
            selectionNameLabel.Text = $"{name}:";

            if (horizontal)
            {
                Controls.Clear();

                layoutPanel = new TableLayoutPanel
                {
                    Dock = fill ? DockStyle.Fill : DockStyle.Top,
                    AutoSize = false,
                    ColumnCount = 2,
                    Padding = new Padding(1, 2, 1, 2),
                };

                selectionNameLabel.AutoSize = true;
                selectionNameLabel.Margin = new Padding(0, 4, 5, 0);
                selectionNameLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;

                comboBox.Anchor = AnchorStyles.Left | AnchorStyles.Top;
                comboBox.AutoSize = true;  // Let it size to content

                layoutPanel.Controls.Add(selectionNameLabel, 0, 0);
                layoutPanel.Controls.Add(comboBox, 1, 0);

                Controls.Add(layoutPanel);
                MinimumSize = new Size(selectionNameLabel.Width + 20, comboBox.Height + layoutPanel.Padding.Vertical);
                Size = new Size(Size.Width, MinimumSize.Height);
            }

            ResumeLayout();
        }
    }
}

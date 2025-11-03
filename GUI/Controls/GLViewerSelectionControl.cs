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

            if (horizontal)
            {
                selectionNameLabel.Text = name;
                Controls.Clear();

                layoutPanel = new TableLayoutPanel
                {
                    Dock = fill ? DockStyle.Fill : DockStyle.Top,
                    AutoSize = !fill,
                    ColumnCount = 2,
                    Padding = new Padding(1, 2, 1, 2),
                };

                layoutPanel.ColumnStyles.Add(new ColumnStyle(SizeType.AutoSize));  // Label column
                layoutPanel.ColumnStyles.Add(fill
                    ? new ColumnStyle(SizeType.Percent, 100F)
                    : new ColumnStyle(SizeType.Absolute)
                );

                selectionNameLabel.AutoSize = true;
                selectionNameLabel.Margin = new Padding(0, 4, 5, 0);
                selectionNameLabel.Anchor = AnchorStyles.Left | AnchorStyles.Top;

                comboBox.AutoSize = !fill;
                comboBox.Anchor = fill
                    ? (AnchorStyles.Left | AnchorStyles.Top | AnchorStyles.Right)
                    : (AnchorStyles.Left | AnchorStyles.Top);

                layoutPanel.Controls.Add(selectionNameLabel, 0, 0);
                layoutPanel.Controls.Add(comboBox, 1, 0);

                Controls.Add(layoutPanel);
                MinimumSize = new Size(selectionNameLabel.Width, comboBox.Height + layoutPanel.Padding.Vertical);
                Size = new Size(Size.Width, MinimumSize.Height);
            }
            else
            {
                selectionNameLabel.Text = $"{name}:";
            }

            ResumeLayout();
        }
    }
}

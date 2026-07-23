using System.Windows.Forms;

namespace GUI.Controls
{
    /// <summary>
    /// Titled, outlined sidebar section. Rows stack in a single auto-sized column inside the
    /// same themed group box the other sidebar sections use, and the whole section follows
    /// whatever height the rows really take.
    /// </summary>
    partial class GLViewerGroupedSectionControl : UserControl
    {
        private GLViewerGroupedSectionControl()
        {
            InitializeComponent();

            // Auto-size does not propagate reliably through docked containers, so the section
            // derives its height from the live table height and the box's measured chrome
            // whenever the rows change size for any reason.
            tableLayout.SizeChanged += (_, _) => FitToContent();
        }

        public GLViewerGroupedSectionControl(string name)
            : this()
        {
            groupBox.Text = name;
        }

        protected override void OnHandleCreated(EventArgs e)
        {
            base.OnHandleCreated(e);
            FitToContent();
        }

        protected override void OnFontChanged(EventArgs e)
        {
            base.OnFontChanged(e);
            FitToContent();
        }

        private void FitToContent()
        {
            // Height minus the display rectangle is the box's own chrome: title band, padding
            // and borders, whatever the current font and DPI make of them.
            var chrome = groupBox.Height - groupBox.DisplayRectangle.Height;
            var desired = tableLayout.Height + chrome + Padding.Vertical;

            if (desired > 0 && Height != desired)
            {
                Height = desired;
            }
        }

        public void AddRow(Control control)
        {
            control.Dock = DockStyle.Fill;
            tableLayout.RowCount++;
            tableLayout.RowStyles.Add(new RowStyle());
            tableLayout.Controls.Add(control, 0, tableLayout.RowCount - 1);
            FitToContent();
        }

        public void ClearRows()
        {
            for (var i = tableLayout.Controls.Count - 1; i >= 0; i--)
            {
                var control = tableLayout.Controls[i];
                tableLayout.Controls.RemoveAt(i);
                control.Dispose();
            }

            tableLayout.RowStyles.Clear();
            tableLayout.RowCount = 0;
            FitToContent();
        }
    }
}

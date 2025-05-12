using System.Windows.Forms;

namespace GUI.Controls
{
    partial class TextControl : ControlPanelView
    {
        protected override Panel ControlsPanel => splitContainer.Panel1;
        public CodeTextBox TextBox { get; }
        public TextControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            TextBox = new CodeTextBox(string.Empty);
            splitContainer.Panel2.Controls.Add(TextBox);
        }
    }
}

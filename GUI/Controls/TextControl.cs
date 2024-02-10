using System.Windows.Forms;

namespace GUI.Controls
{
    partial class TextControl : ControlPanelView
    {
        protected override Panel ControlsPanel => controlsPanel;
        public CodeTextBox TextBox { get; }
        public TextControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            TextBox = new CodeTextBox("");
            textContainer.Controls.Add(TextBox);
        }
    }
}

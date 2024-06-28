using System.Windows.Forms;

namespace GUI.Controls
{
    partial class TextControl : ControlPanelView
    {
        protected override Panel ControlsPanel => controlsPanel;
        public CodeTextBox BetterTextBox { get; }
        public TextControl()
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            BetterTextBox = new CodeTextBox("");
            textContainer.Controls.Add(BetterTextBox);
        }
    }
}

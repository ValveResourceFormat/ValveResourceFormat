using System.Windows.Forms;
using static GUI.Controls.CodeTextBox;

namespace GUI.Controls
{
    partial class TextControl : UserControl
    {
        public Panel ControlsPanel => splitContainer.Panel1;
        public Panel MainPanel => splitContainer.Panel2;
        public CodeTextBox TextBox { get; }

        public TextControl(HighlightLanguage highlightSyntax = HighlightLanguage.Default)
        {
            InitializeComponent();
            Dock = DockStyle.Fill;
            TextBox = new CodeTextBox(string.Empty, highlightSyntax);
            splitContainer.Panel2.Controls.Add(TextBox);
        }

        public void AddControl(Control control)
        {
            ControlsPanel.Controls.Add(control);
        }
    }
}

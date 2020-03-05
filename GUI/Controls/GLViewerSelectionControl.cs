using System.Windows.Forms;

namespace GUI.Controls
{
    public partial class GLViewerSelectionControl : UserControl
    {
        public ComboBox ComboBox => comboBox;

        private GLViewerSelectionControl()
        {
            InitializeComponent();
        }

        public GLViewerSelectionControl(string name)
            : this()
        {
            selectionNameLabel.Text = $"{name}:";
        }
    }
}

using System.Windows.Forms;

namespace GUI.Controls
{
    public partial class GLViewerSelectionControl : UserControl
    {
        public ComboBox ComboBox => comboBox;

        public GLViewerSelectionControl()
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

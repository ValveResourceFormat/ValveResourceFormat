using System.Windows.Forms;
using DarkModeForms;

namespace GUI.Controls
{
    partial class GLViewerSelectionControl : UserControl
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

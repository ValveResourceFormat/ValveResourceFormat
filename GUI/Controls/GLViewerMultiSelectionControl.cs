using System.Windows.Forms;

namespace GUI.Controls
{
    partial class GLViewerMultiSelectionControl : UserControl
    {
        public CheckedListBox CheckedListBox => checkedListBox;

        private GLViewerMultiSelectionControl()
        {
            InitializeComponent();
        }

        public GLViewerMultiSelectionControl(string name)
            : this()
        {
            groupBox.Text = name;
        }
    }
}

using System.Windows.Forms;

namespace GUI.Controls
{
    public partial class GLViewerMultiSelectionControl : UserControl
    {
        public CheckedListBox CheckedListBox => checkedListBox;

        private GLViewerMultiSelectionControl()
        {
            InitializeComponent();
        }

        public GLViewerMultiSelectionControl(string name)
            : this()
        {
            selectionNameLabel.Text = $"{name}:";
        }
    }
}

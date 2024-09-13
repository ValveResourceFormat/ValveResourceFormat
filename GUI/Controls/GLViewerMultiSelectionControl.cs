using System.Windows.Forms;

namespace GUI.Controls
{
    partial class GLViewerMultiSelectionControl : UserControl
    {
        public BetterCheckedListBox BetterCheckedListBox => checkedListBox;

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

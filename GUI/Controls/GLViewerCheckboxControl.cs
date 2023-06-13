using System.Windows.Forms;

namespace GUI.Controls
{
    partial class GLViewerCheckboxControl : UserControl
    {
        public CheckBox CheckBox => checkBox;

        private GLViewerCheckboxControl()
        {
            InitializeComponent();
        }

        public GLViewerCheckboxControl(string name, bool isChecked)
            : this()
        {
            checkBox.Text = name;
            checkBox.Checked = isChecked;
        }
    }
}

using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class PromptForm : Form
    {
        public string ResultText => inputTextBox.Text;

        public PromptForm(string title)
        {
            InitializeComponent();

            Text = title;
            textLabel.Text = string.Concat(title, ":");

            MainForm.ThemeManager.RegisterControl(this);
        }

        private void PromptForm_Load(object sender, EventArgs e)
        {
            ActiveControl = inputTextBox;
        }
    }
}

using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class PromptForm : ThemedForm
    {
        public string ResultText => inputTextBox.Text;

        public PromptForm(string title)
        {
            InitializeComponent();

            Text = title;
            textLabel.Text = string.Concat(title, ":");
        }

        private void PromptForm_Load(object sender, EventArgs e)
        {
            ActiveControl = inputTextBox;
        }
    }
}

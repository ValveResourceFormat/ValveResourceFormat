using System.Windows.Forms;
using DarkModeForms;

namespace GUI.Forms
{
    public partial class PromptForm : Form
    {
        public string ResultText => inputTextBox.Text;

        public PromptForm(string title)
        {
            InitializeComponent();

            MainForm.DarkModeCS.Style(this);

            Text = title;
            textLabel.Text = string.Concat(title, ":");
        }

        private void PromptForm_Load(object sender, EventArgs e)
        {
            ActiveControl = inputTextBox;
        }
    }
}

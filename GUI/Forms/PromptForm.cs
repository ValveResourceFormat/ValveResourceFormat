namespace GUI.Forms
{
    public partial class PromptForm : ThemedForm
    {
        public string ResultText => inputTextBox.Text;

        public PromptForm(string title, string initialText = "")
        {
            InitializeComponent();

            Text = title;
            textLabel.Text = string.Concat(title, ":");
            inputTextBox.Text = initialText;
        }

        private void PromptForm_Load(object sender, EventArgs e)
        {
            ActiveControl = inputTextBox;
            inputTextBox.SelectAll();
        }
    }
}

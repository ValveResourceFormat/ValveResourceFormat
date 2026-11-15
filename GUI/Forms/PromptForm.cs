namespace GUI.Forms
{
    public partial class PromptForm : ThemedForm
    {
        public string ResultText => inputTextBox.Text;

        public PromptForm(string title, string label, string initialText = "")
        {
            InitializeComponent();

            cancelButton.Text = Locale.Dialog_Cancel;
            submitButton.Text = Locale.Prompt_Submit;

            Text = title;
            textLabel.Text = label;
            inputTextBox.Text = initialText;
        }

        private void PromptForm_Load(object sender, EventArgs e)
        {
            ActiveControl = inputTextBox;
            inputTextBox.SelectAll();
        }
    }
}

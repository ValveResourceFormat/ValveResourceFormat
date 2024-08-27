using System.Windows.Forms;

namespace GUI.Forms
{
    partial class SearchForm : Form
    {
        /// <summary>
        /// Gets whatever text was entered by the user in the search textbox.
        /// </summary>
        public string SearchText
        {
            get => findTextBox.Text;
            set => findTextBox.Text = value;
        }

        /// <summary>
        /// Gets whatever options was selected by the user in the search type combobox.
        /// </summary>
        public SearchType SelectedSearchType => ((SearchTypeItem)searchTypeComboBox.SelectedItem).Type;

        public SearchForm()
        {
            InitializeComponent();

            searchTypeComboBox.ValueMember = "Id";
            searchTypeComboBox.DisplayMember = "Name";
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Name (Partial Match)", SearchType.FileNamePartialMatch));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Name (Exact Match)", SearchType.FileNameExactMatch));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Full Path", SearchType.FullPath));
            searchTypeComboBox.Items.Add(new SearchTypeItem("Regex", SearchType.Regex));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Contents (Case Sensitive)", SearchType.FileContents));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Contents Hex Bytes", SearchType.FileContentsHex));
            searchTypeComboBox.SelectedIndex = 0;

            MainForm.ThemeManager.RegisterControl(this);
        }

        /// <summary>
        /// On form load, setup the combo box search options and set the textbox as the focused control.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void SearchForm_Load(object sender, EventArgs e)
        {
            ActiveControl = findTextBox;
        }
    }
}

using System;
using System.Windows.Forms;

namespace GUI.Forms
{
    public partial class SearchForm : Form
    {
        /// <summary>
        /// Contains whatever text was entered by the user in the search textbox.
        /// </summary>
        public string SearchText
        {
            get { return findTextBox.Text; }
        }

        /// <summary>
        /// Contains whatever options was selected by the user in the search type combobox.
        /// </summary>
        public SearchType SelectedSearchType
        {
            get
            {
                var selectedItem = (SearchTypeItem)searchTypeComboBox.SelectedItem;
                return (SearchType)selectedItem.Id;
            }
        }

        public SearchForm()
        {
            InitializeComponent();

            searchTypeComboBox.ValueMember = "Id";
            searchTypeComboBox.DisplayMember = "Name";
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Name (Partial Match)", (int)SearchType.FileNamePartialMatch));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Name (Exact Match)", (int)SearchType.FileNameExactMatch));
            searchTypeComboBox.Items.Add(new SearchTypeItem("File Full Path", (int)SearchType.FullPath));
            searchTypeComboBox.SelectedIndex = 0;
        }

        private void findButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.OK;
            Close();
        }

        private void cancelButton_Click(object sender, EventArgs e)
        {
            DialogResult = DialogResult.Cancel;
            Close();
        }

        /// <summary>
        /// On form load, setup the combo box search options and set the textbox as the focused control.
        /// </summary>
        /// <param name="sender"></param>
        /// <param name="e"></param>
        private void SearchForm_Load(object sender, EventArgs e)
        {
            ActiveControl = findTextBox;
        }
    }
}
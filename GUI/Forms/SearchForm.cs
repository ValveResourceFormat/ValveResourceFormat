using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using GUI.Utils;

namespace GUI.Forms
{
    partial class SearchForm : ThemedForm
    {
        internal static readonly StringComparer NumericComparer =
            StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        private record FilterKeyEntry(string Key, SortedSet<string> Values)
        {
            public override string ToString() => Key;
        }

        private int filterLoadVersion;
        private int lastPopulatedKeysHash;

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
        public SearchType SelectedSearchType => searchTypeComboBox.SelectedItem is SearchTypeItem item ? item.Type : SearchType.FileNamePartialMatch;

        /// <summary>
        /// Gets the selected SearchableUserData filter key, or null if no filter is selected.
        /// </summary>
        public string? SelectedFilterKey => filterKeyComboBox.SelectedItem is FilterKeyEntry entry ? entry.Key : null;

        /// <summary>
        /// Gets the selected SearchableUserData filter value, or null if any value is accepted.
        /// </summary>
        public string? SelectedFilterValue => filterValueComboBox.SelectedIndex > 0 ? filterValueComboBox.Items[filterValueComboBox.SelectedIndex] as string : null;

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

            filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;
        }

        /// <summary>
        /// Sets the available SearchableUserData filter keys from a task.
        /// If the task is already completed, populates immediately.
        /// Otherwise shows a loading state and populates when the task completes.
        /// </summary>
        public void SetSearchableUserDataKeys(Task<Dictionary<string, SortedSet<string>>?> keysTask)
        {
            // Skip repopulating if the same data is already loaded (preserves user's filter selection)
            if (keysTask.IsCompletedSuccessfully
                && keysTask.Result?.GetHashCode() == lastPopulatedKeysHash)
            {
                return;
            }

            var version = ++filterLoadVersion;

            filterKeyComboBox.SelectedIndexChanged -= FilterKeyComboBox_SelectedIndexChanged;

            if (keysTask.IsCompleted)
            {
                PopulateFilterKeys(keysTask.IsCompletedSuccessfully ? keysTask.Result : null);
                filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;
                return;
            }

            // Show loading state
            filterKeyComboBox.BeginUpdate();
            filterKeyComboBox.Items.Clear();
            filterKeyComboBox.Items.Add("Loading asset info\u2026");
            filterKeyComboBox.SelectedIndex = 0;
            filterKeyComboBox.Enabled = false;
            filterKeyComboBox.EndUpdate();

            filterValueComboBox.Items.Clear();
            filterValueComboBox.Visible = false;

            filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;

            keysTask.ContinueWith(t =>
            {
                if (!IsDisposed && filterLoadVersion == version)
                {
                    BeginInvoke(() =>
                    {
                        filterKeyComboBox.SelectedIndexChanged -= FilterKeyComboBox_SelectedIndexChanged;
                        PopulateFilterKeys(t.IsCompletedSuccessfully ? t.Result : null);
                        filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;
                    });
                }
            }, TaskScheduler.Default);
        }

        private void PopulateFilterKeys(Dictionary<string, SortedSet<string>>? keys)
        {
            lastPopulatedKeysHash = keys?.GetHashCode() ?? 0;
            var hasKeys = keys != null && keys.Count > 0;

            filterKeyComboBox.BeginUpdate();
            filterKeyComboBox.Items.Clear();

            if (hasKeys)
            {
                var sorted = keys!.OrderBy(kv => kv.Key, NumericComparer).Select(kv => (object)new FilterKeyEntry(kv.Key, kv.Value)).ToArray();

                filterKeyComboBox.Items.Add("(No filter)");
                filterKeyComboBox.Items.AddRange(sorted);
                filterKeyComboBox.SelectedIndex = 0;
                filterKeyComboBox.Enabled = true;
            }
            else
            {
                filterKeyComboBox.Items.Add("No asset info");
                filterKeyComboBox.SelectedIndex = 0;
                filterKeyComboBox.Enabled = false;
            }

            filterKeyComboBox.EndUpdate();

            filterValueComboBox.Items.Clear();
            filterValueComboBox.Visible = false;
        }

        private void FilterKeyComboBox_SelectedIndexChanged(object? sender, EventArgs e)
        {
            filterValueComboBox.BeginUpdate();
            filterValueComboBox.Items.Clear();

            if (filterKeyComboBox.SelectedItem is not FilterKeyEntry entry)
            {
                filterValueComboBox.EndUpdate();
                filterValueComboBox.Visible = false;
                return;
            }

            filterValueComboBox.Items.Add("(Any value)");
            filterValueComboBox.Items.AddRange([.. entry.Values]);
            filterValueComboBox.SelectedIndex = 0;
            filterValueComboBox.EndUpdate();
            filterValueComboBox.Visible = true;
        }

        protected override void OnCreateControl()
        {
            base.OnCreateControl();

            findTextBox.BackColor = Themer.CurrentThemeColors.AppMiddle;
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

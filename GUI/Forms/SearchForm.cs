using System.Drawing;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Windows.Win32;
using Windows.Win32.Foundation;

namespace GUI.Forms
{
    partial class SearchForm : ThemedForm
    {
        internal static readonly StringComparer NumericComparer =
            StringComparer.Create(CultureInfo.InvariantCulture, CompareOptions.IgnoreCase | CompareOptions.NumericOrdering);

        internal static readonly string[] SearchTypeNames = ["File name", "Full path", "File contents", "File contents hex bytes"];

        private record FilterKeyEntry(string Key, SortedSet<string> Values)
        {
            public override string ToString() => Key;
        }

        private readonly TaskScheduler uiTaskScheduler;
        private int filterLoadVersion;
        private int lastPopulatedKeysHash;
        private VirtualPackageNode? folder;

        /// <summary>
        /// Gets the search as entered by the user.
        /// </summary>
        public SearchRequest Request => new(
            new(findTextBox.Text, (PackageSearchType)searchTypeComboBox.SelectedIndex, matchCaseCheckBox.Checked, wholeWordCheckBox.Checked, regexCheckBox.Checked),
            onlyInFolderCheckBox.Checked ? folder : null,
            extensionComboBox.SelectedIndex > 0 ? (string)extensionComboBox.SelectedItem! : null,
            filterKeyComboBox.SelectedItem is FilterKeyEntry entry ? entry.Key : null,
            filterValueComboBox.SelectedIndex > 0 ? filterValueComboBox.Items[filterValueComboBox.SelectedIndex] as string : null);

        public SearchForm()
        {
            InitializeComponent();
            uiTaskScheduler = TaskScheduler.FromCurrentSynchronizationContext();

            searchTypeComboBox.Items.AddRange(SearchTypeNames);
            searchTypeComboBox.SelectedIndex = 0;

            filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;
            filterKeyComboBox.SelectedIndexChanged += (_, _) => UpdateState();
            findTextBox.TextChanged += (_, _) => UpdateState();
            findTextBox.HandleCreated += (_, _) => PInvoke.SendMessage((HWND)findTextBox.Handle, PInvoke.EM_SETMARGINS, PInvoke.EC_LEFTMARGIN, this.AdjustForDPI(5));
            searchTypeComboBox.SelectedIndexChanged += (_, _) => UpdateState();
            matchCaseCheckBox.CheckedChanged += (_, _) => UpdateState();
            wholeWordCheckBox.CheckedChanged += (_, _) => UpdateState();
            regexCheckBox.CheckedChanged += (_, _) => UpdateState();
            onlyInFolderCheckBox.CheckedChanged += (_, _) => UpdateState();
            extensionComboBox.SelectedIndexChanged += (_, _) => UpdateState();
        }

        private void UpdateState()
        {
            var request = Request;
            var error = PackageSearch.GetError(request.Query);

            matchCaseCheckBox.Enabled = wholeWordCheckBox.Enabled = regexCheckBox.Enabled = request.Query.Type != PackageSearchType.FileContentsHex;
            hintLabel.Text = error;
            findButton.Enabled = error == null && request is not { Query.Text: "", Folder: null, Extension: null, FilterKey: null };

            var gap = filterKeyLabel.Top - extensionComboBox.Bottom;
            var margin = ClientSize.Height - findButton.Bottom;

            hintLabel.Visible = error != null;
            hintLabel.Top = (filterKeyComboBox.SelectedItem is FilterKeyEntry ? filterValueComboBox : filterKeyComboBox).Bottom + gap;
            findButton.Top = cancelButton.Top = error != null ? hintLabel.Bottom + gap : hintLabel.Top;
            ClientSize = new Size(ClientSize.Width, findButton.Bottom + margin);
        }

        /// <summary>
        /// Sets the package to search, listing the extensions of its files.
        /// </summary>
        /// <param name="searchFolder">The folder that "Search only in" limits the search to, or <see langword="null"/> when no folder is selected.</param>
        /// <param name="onlyInFolder">Whether to tick "Search only in", otherwise it stays as it was last used.</param>
        public void SetPackage(Package package, VirtualPackageNode? searchFolder, bool onlyInFolder)
        {
            folder = searchFolder;
            onlyInFolderCheckBox.Enabled = folder != null;
            onlyInFolderCheckBox.FlatStyle = folder != null ? FlatStyle.Standard : FlatStyle.System;
            onlyInFolderCheckBox.Text = folder != null ? $"Search only in: {PackageViewer.GetFolderPath(folder)}/" : "Search only in: no folder selected";
            onlyInFolderCheckBox.Checked |= onlyInFolder;

            var selected = extensionComboBox.Text;
            extensionComboBox.BeginUpdate();
            extensionComboBox.Items.Clear();
            extensionComboBox.Items.Add("Any extension");
            extensionComboBox.Items.AddRange(package.Entries?.Keys.Order(NumericComparer).ToArray() ?? []);
            extensionComboBox.SelectedIndex = Math.Max(0, extensionComboBox.FindStringExact(selected));
            extensionComboBox.EndUpdate();

            UpdateState();
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
                    filterKeyComboBox.SelectedIndexChanged -= FilterKeyComboBox_SelectedIndexChanged;
                    PopulateFilterKeys(t.IsCompletedSuccessfully ? t.Result : null);
                    filterKeyComboBox.SelectedIndexChanged += FilterKeyComboBox_SelectedIndexChanged;
                }
            }, uiTaskScheduler);
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
        /// On form load, set the search textbox as the focused control.
        /// </summary>
        /// <param name="sender">Object which raised event.</param>
        /// <param name="e">Event data.</param>
        private void SearchForm_Load(object sender, EventArgs e)
        {
            ActiveControl = findTextBox;
        }
    }
}

using System.Globalization;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValveResourceFormat;

namespace GUI.Controls
{
    /// <summary>
    /// Lists every block of a type that a resource repeats, such as the per mesh blocks of a model,
    /// and shows the selected one, instead of a tab per block.
    /// </summary>
    class RepeatedBlocksViewer : TextControl
    {
        private readonly Action<Block, Control> populateBlock;

        public RepeatedBlocksViewer(List<(int Index, Block Block)> blocks, Dictionary<int, string> blockNames, Action<Block, Control> populateBlock)
        {
            this.populateBlock = populateBlock;

            // The selected block brings its own control, which may not be text
            MainPanel.Controls.Remove(TextBox);

            var blockListView = new BetterListView
            {
                View = View.Details,
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                MultiSelect = false,
                HideSelection = false,
                ShowItemToolTips = true,
            };
            blockListView.ItemSelectionChanged += OnItemSelectionChanged;

            // The first column stretches to fill the list
            blockListView.Columns.Add("Name");
            blockListView.Columns.Add("Block");
            blockListView.Columns.Add("Size");

            blockListView.BeginUpdate();

            foreach (var (index, block) in blocks)
            {
                var name = blockNames.GetValueOrDefault(index, string.Empty);
                var item = new ListViewItem(name)
                {
                    Tag = block,
                    ToolTipText = name,
                };
                item.SubItems.Add(index.ToString(CultureInfo.InvariantCulture));
                item.SubItems.Add(HumanReadableByteSizeFormatter.Format(block.Size));
                blockListView.Items.Add(item);
            }

            blockListView.EndUpdate();

            Themer.ThemeControl(blockListView);
            AddControl(blockListView);

            blockListView.Items[0].Selected = true;
        }

        private void OnItemSelectionChanged(object? sender, ListViewItemSelectionChangedEventArgs e)
        {
            if (!e.IsSelected || e.Item?.Tag is not Block block)
            {
                return;
            }

            MainPanel.SuspendLayout();

            try
            {
                while (MainPanel.Controls.Count > 0)
                {
                    MainPanel.Controls[0].Dispose();
                }

                populateBlock(block, MainPanel);
            }
            finally
            {
                MainPanel.ResumeLayout(true);
            }
        }
    }
}

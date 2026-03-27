using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Controls
{
    class ChoreoViewer : TextControl
    {
        private readonly ChoreoSceneFileData choreoDataList;
        private readonly ListView fileListView;

        public ChoreoViewer(Resource resource)
        {
            var dataBlock = (ChoreoSceneFileData?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(dataBlock);
            choreoDataList = dataBlock;

            var fileName = Path.GetFileNameWithoutExtension(resource.FileName) + ".vcdlist";

            fileListView = new ListView
            {
                View = View.Details,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            fileListView.ItemSelectionChanged += FileListView_ItemSelectionChanged;

            fileListView.Columns.Add("Name", 250);
            fileListView.Columns.Add("Version");

            AddListItem(null, fileName, choreoDataList.Version);
            for (var i = 0; i < choreoDataList.Scenes.Length; i++)
            {
                var scene = choreoDataList.Scenes[i];
                AddListItem(i, scene.Name ?? string.Empty, scene.Version);
            }

            AddControl(fileListView);
        }

        private void AddListItem(int? index, string name, int version)
        {
            var item = fileListView.Items.Add(new ListViewItem
            {
                Text = name,
                ToolTipText = name,
            });

            var versionString = version.ToString(CultureInfo.InvariantCulture);
            item.SubItems.Add(versionString);

            item.Tag = index;
        }

        private void FileListView_ItemSelectionChanged(object? sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0)
            {
                TextBox.Text = "";
                return;
            }

            var selectedItem = fileListView.SelectedItems[0];
            var selectedScene = (int?)selectedItem.Tag;

            if (selectedScene == null)
            {
                ShowVcdList();
            }
            else
            {
                ShowVcd(selectedScene.Value);
            }
        }

        private void ShowVcdList()
        {
            var sb = new StringBuilder();
            foreach (var scene in choreoDataList.Scenes)
            {
                sb.AppendLine(scene.Name);
            }
            TextBox.Text = sb.ToString();
        }

        private void ShowVcd(int index)
        {
            var scene = choreoDataList.Scenes[index];
            var kv = new KV3File(scene.ToKeyValues());
            TextBox.Text = kv.ToString();
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                fileListView?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

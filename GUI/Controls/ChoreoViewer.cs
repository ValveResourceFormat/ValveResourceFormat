using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Controls
{
    class ChoreoViewer : TextControl
    {
        private readonly ChoreoSceneFileData choreoDataList;
        private readonly BetterListView fileListView;

        public ChoreoViewer(Resource resource)
        {
            var dataBlock = (ChoreoSceneFileData?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(dataBlock);
            choreoDataList = dataBlock;

            var fileName = Path.GetFileNameWithoutExtension(resource.FileName) + ".vcdlist";

            fileListView = new BetterListView
            {
                View = View.Details,
                Anchor = AnchorStyles.Left | AnchorStyles.Right | AnchorStyles.Top,
                Dock = DockStyle.Fill,
                FullRowSelect = true,
                MultiSelect = false,
                ShowItemToolTips = true
            };
            fileListView.ItemSelectionChanged += FileListView_ItemSelectionChanged;

            // The first column stretches to fill the list
            fileListView.Columns.Add("Name");
            fileListView.Columns.Add("Version");
            fileListView.Columns.Add("Duration (s)");
            fileListView.Columns.Add("Sound duration (s)");
            fileListView.Columns.Add("Has sounds");

            AddListItem(null, fileName, choreoDataList.Version, null);
            for (var i = 0; i < choreoDataList.Scenes.Length; i++)
            {
                var scene = choreoDataList.Scenes[i];
                AddListItem(i, scene.Name ?? string.Empty, scene.Version, scene);
            }

            Themer.ThemeControl(fileListView);
            AddControl(fileListView);
        }

        private void AddListItem(int? index, string name, int version, ChoreoScene? scene)
        {
            var item = fileListView.Items.Add(new ListViewItem
            {
                Text = name,
                ToolTipText = name,
            });

            var versionString = version.ToString(CultureInfo.InvariantCulture);
            item.SubItems.Add(versionString);

            item.SubItems.Add(scene == null ? string.Empty : FormatMilliseconds(scene.Duration));
            item.SubItems.Add(scene == null ? string.Empty : FormatMilliseconds(scene.SoundDuration));
            item.SubItems.Add(scene == null ? string.Empty : (scene.HasSounds ? "Yes" : "No"));

            item.Tag = index;
        }

        private static string FormatMilliseconds(int milliseconds)
        {
            return (milliseconds / 1000f).ToString("0.000", CultureInfo.InvariantCulture);
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
            TextBox.Text = scene.ToKeyValues().ToKV3String();
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

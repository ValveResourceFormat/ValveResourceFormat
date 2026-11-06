using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Types.GLViewers;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.Renderer.Choreo;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.ResourceTypes.Choreo;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Controls
{
    class ChoreoViewer : TextControl
    {
        private readonly ChoreoSceneFileData choreoDataList;
        private readonly VrfGuiContext vrfGuiContext;
        private readonly BetterListView fileListView;

        public ChoreoViewer(VrfGuiContext vrfGuiContext, Resource resource)
        {
            var dataBlock = (ChoreoSceneFileData?)resource.DataBlock;
            ArgumentNullException.ThrowIfNull(dataBlock);
            choreoDataList = dataBlock;
            this.vrfGuiContext = vrfGuiContext;

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
            fileListView.ItemActivate += FileListView_ItemActivate;

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

        private void FileListView_ItemActivate(object? sender, EventArgs e)
        {
            if (fileListView.SelectedItems.Count == 0 || fileListView.SelectedItems[0].Tag is not int index)
            {
                return;
            }

            var scene = choreoDataList.Scenes[index];
            var modelFiles = vrfGuiContext.FindFilesWithExtension("vmdl_c").ToList();

            var match = ChoreoActorMatcher.FindModel(scene, modelFiles);

            if (match == null)
            {
                Log.Warn(nameof(ChoreoViewer), $"Found no model for the actors of {scene.Name}");
                return;
            }

            var (model, actor) = match.Value;
            var foundFile = vrfGuiContext.FindFileWithContext(model);
            if (foundFile.Context == null)
            {
                return;
            }

            foundFile.Context.GLPostLoadAction = (viewerControl) =>
            {
                if (viewerControl is GLModelViewer modelViewer)
                {
                    modelViewer.SetStartScene(scene.Name, actor.Name);
                }
            };

            Program.MainForm.OpenFile(foundFile.Context, foundFile.PackageEntry);
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

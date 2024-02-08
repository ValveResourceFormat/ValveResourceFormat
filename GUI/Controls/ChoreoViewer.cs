using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.ResourceTypes;
using ValveResourceFormat.Serialization.KeyValues;

namespace GUI.Controls
{
    class ChoreoViewer : TextControl
    {
        private ChoreoDataList choreoDataList;
        private ComboBox sceneComboBox;
        private Label fileVersionLabel;
        public ChoreoViewer(Resource resource)
        {
            choreoDataList = (ChoreoDataList)resource.DataBlock;

            var sceneNames = choreoDataList.Scenes.Select(s => s.Name).ToArray();
            sceneComboBox = AddSelection("Files", (name, index) =>
            {
                if (index == 0)
                {
                    ShowVcdList();
                }
                else
                {
                    ShowVcd(index - 1);
                }
            });
            sceneComboBox.Items.Add(".vcdlist");
            sceneComboBox.Items.AddRange(sceneNames);

            fileVersionLabel = new Label();
            AddControl(fileVersionLabel);
        }
        private void ShowVcdList()
        {
            var sb = new StringBuilder();
            foreach (var scene in choreoDataList.Scenes)
            {
                sb.AppendLine(scene.Name);
            }
            TextBox.Text = sb.ToString();

            SetVersion(choreoDataList.Unk1);
        }
        private void ShowVcd(int index)
        {
            var scene = choreoDataList.Scenes[index];
            var kv = new KV3File(scene.ToKeyValues());
            TextBox.Text = kv.ToString();

            SetVersion(scene.Version);
        }
        private void SetVersion(int version)
        {
            fileVersionLabel.Text = $"File version: {version}";
        }

        protected override void Dispose(bool disposing)
        {
            if (disposing)
            {
                sceneComboBox?.Dispose();
                fileVersionLabel?.Dispose();
            }

            base.Dispose(disposing);
        }
    }
}

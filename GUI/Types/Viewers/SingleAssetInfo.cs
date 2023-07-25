using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GUI.Utils;
using ValveKeyValue;

namespace GUI.Types.Viewers
{
    class SingleAssetInfo
    {
        record FileReference(string Type, string File);

        public static TabPage Create(VrfGuiContext guiContext, string filePath)
        {
            var folder = Path.GetDirectoryName(guiContext.FileName);

            if (guiContext.ToolsAssetInfo == null)
            {
                var path = Path.Join(folder, "readonly_tools_asset_info.bin");

                guiContext.ToolsAssetInfo = new ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo();
                guiContext.ToolsAssetInfo.Read(path);
            }

            if (!guiContext.ToolsAssetInfo.Files.TryGetValue(filePath, out var assetInfo))
            {
                var gameRootPath = string.Concat(Path.GetFileName(folder), "/", filePath);

                foreach (var (filePathTemp, assetInfoTemp) in guiContext.ToolsAssetInfo.Files)
                {
                    if (assetInfoTemp.SearchPathsGameRoot.Exists(f => f.Filename == gameRootPath))
                    {
                        filePath = filePathTemp;
                        assetInfo = assetInfoTemp;
                        break;
                    }
                }
            }

            if (assetInfo == null)
            {
                MessageBox.Show(
                    $"Failed to find tools asset info for {filePath}",
                    "No asset info found",
                    MessageBoxButtons.OK,
                    MessageBoxIcon.Information
                );
                return null;
            }

            var referencedBy = new List<FileReference>();

            foreach (var (filePathTemp, assetInfoTemp) in guiContext.ToolsAssetInfo.Files)
            {
                if (assetInfoTemp.ChildResources.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Child Resource", filePathTemp));
                }

                if (assetInfoTemp.ExternalReferences.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("External Reference", filePathTemp));
                }

                if (assetInfoTemp.WeakReferences.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Weak Reference", filePathTemp));
                }

                if (assetInfoTemp.AdditionalRelatedFiles.Contains(filePath))
                {
                    referencedBy.Add(new FileReference("Additional Related File", filePathTemp));
                }

                if (assetInfoTemp.InputDependencies.Exists(f => f.Filename == filePath))
                {
                    referencedBy.Add(new FileReference("Input Dependency", filePathTemp));
                }

                if (assetInfoTemp.AdditionalInputDependencies.Exists(f => f.Filename == filePath))
                {
                    referencedBy.Add(new FileReference("Additional Input Dependency", filePathTemp));
                }
            }

            var resTabs = new TabControl
            {
                Dock = DockStyle.Fill,
            };
            var parentTab = new TabPage(Path.GetFileName(filePath))
            {
                ToolTipText = filePath
            };
            parentTab.Controls.Add(resTabs);

            // Info
            var tab = new TabPage("Info");

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, assetInfo, "Asset Info");

            var infoControl = new MonospaceTextBox
            {
                Text = Encoding.UTF8.GetString(ms.ToArray()).ReplaceLineEndings(),
                Dock = DockStyle.Fill
            };

            tab.Controls.Add(infoControl);
            resTabs.TabPages.Add(tab);

            // Referenced by
            tab = new TabPage("Referenced by");

            var referencedContorl = new DataGridView
            {
                Dock = DockStyle.Fill,
                AutoGenerateColumns = true,
                AutoSize = true,
                ReadOnly = true,
                AllowUserToAddRows = false,
                AutoSizeColumnsMode = DataGridViewAutoSizeColumnsMode.Fill,
                DataSource = new BindingSource(new BindingList<FileReference>(referencedBy), null),
            };

            tab.Controls.Add(referencedContorl);
            resTabs.TabPages.Add(tab);

            return parentTab;
        }
    }
}

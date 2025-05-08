using System.ComponentModel;
using System.Globalization;
using System.IO;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers
{
    class SingleAssetInfo
    {
        record FileReference(string Type, string File);

        public static TabPage Create(VrfGuiContext guiContext, PackageEntry entry)
        {
            var folder = Path.GetDirectoryName(guiContext.FileName);
            var filePath = entry.GetFullPath();

            if (guiContext.ToolsAssetInfo == null)
            {
                var path = Path.Join(folder, "readonly_tools_asset_info.bin");

                if (!File.Exists(path)) // Check parent folder if trying to load asset info in /maps/
                {
                    path = Path.Join(Path.GetDirectoryName(folder), "readonly_tools_asset_info.bin");
                }

                guiContext.ToolsAssetInfo = new ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo();

                if (File.Exists(path))
                {
                    guiContext.ToolsAssetInfo.Read(path);
                }
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

            // If we didn't find exact match in the tools info, try to find the same file without the "_c" suffix
            if (assetInfo == null && filePath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var filePathUncompiled = filePath[..^2];

                if (!guiContext.ToolsAssetInfo.Files.TryGetValue(filePathUncompiled, out assetInfo))
                {
                    var gameRootPath = string.Concat(Path.GetFileName(folder), "/", filePathUncompiled);

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
            }

            var resTabs = new ThemedTabControl
            {
                Dock = DockStyle.Fill,
            };
            var parentTab = new TabPage(Path.GetFileName(filePath))
            {
                ToolTipText = filePath
            };
            parentTab.Controls.Add(resTabs);

            var tab = new TabPage("File");

            var fileInfo = new StringBuilder();

            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Name: {filePath}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"CRC: {entry.CRC32:X2}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Archive: {entry.ArchiveIndex}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Offset: {entry.Offset}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Size: {entry.Length} ({HumanReadableByteSizeFormatter.Format(entry.Length)})");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Preloaded bytes: {entry.SmallData.Length}");

            var fileControl = new CodeTextBox(fileInfo.ToString());

            tab.Controls.Add(fileControl);
            resTabs.TabPages.Add(tab);

            if (assetInfo == null)
            {
                return parentTab;
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

            // Info
            tab = new TabPage("Info");

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, assetInfo, "Asset Info");

            var infoControl = new CodeTextBox(Encoding.UTF8.GetString(ms.ToArray()));
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
                DataSource = new BindingSource(new BindingList<FileReference>(referencedBy), null!),
            };

            tab.Controls.Add(referencedContorl);
            resTabs.TabPages.Add(tab);

            return parentTab;
        }
    }
}

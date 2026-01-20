using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveKeyValue;
using ValveResourceFormat.Blocks;
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
                else
                {
                    filePath = filePathUncompiled;
                }
            }

            var parentTab = new ThemedTabPage(Path.GetFileName(filePath))
            {
                ToolTipText = filePath
            };

            var fileInfo = new StringBuilder();

            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Name: {filePath}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"CRC: {entry.CRC32:X2}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Archive: {entry.ArchiveIndex}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Offset: {entry.Offset}");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Size: {entry.Length} ({HumanReadableByteSizeFormatter.Format(entry.Length)})");
            fileInfo.AppendLine(CultureInfo.InvariantCulture, $"Preloaded bytes: {entry.SmallData.Length}");

            var fileControl = new CodeTextBox(fileInfo.ToString());

            if (assetInfo == null)
            {
                fileControl.Dock = DockStyle.Fill;
                parentTab.Controls.Add(fileControl);
                return parentTab;
            }

            var mainSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
            };
            parentTab.Controls.Add(mainSplitter);

            var leftSplitter = new SplitContainer
            {
                Dock = DockStyle.Fill,
                Orientation = Orientation.Horizontal,
            };
            mainSplitter.Panel1.Controls.Add(leftSplitter);

            fileControl.Dock = DockStyle.Fill;
            leftSplitter.Panel1.Controls.Add(fileControl);

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

            var externalReferences = referencedBy
                .Select(static r => new ResourceExtRefList.ResourceReferenceInfo { Name = r.File })
                .DistinctBy(static x => x.Name)
                .ToList();
            var referencedControl = Resource.BuildExternalRefTree(guiContext, externalReferences);
            leftSplitter.Panel2.Controls.Add(referencedControl);

            using var ms = new MemoryStream();
            KVSerializer.Create(KVSerializationFormat.KeyValues1Text).Serialize(ms, assetInfo, "Asset Info");

            var infoControl = new CodeTextBox(Encoding.UTF8.GetString(ms.ToArray()))
            {
                Dock = DockStyle.Fill,
            };
            mainSplitter.Panel2.Controls.Add(infoControl);

            mainSplitter.SplitterDistance = mainSplitter.Width / 2;
            leftSplitter.SplitterDistance = leftSplitter.Height / 2;

            return parentTab;
        }
    }
}

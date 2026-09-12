using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Utils;
using ValveKeyValue;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Types.Viewers
{
    class SingleAssetInfo
    {
        public static TabPage Create(VrfGuiContext guiContext, PackageEntry entry)
        {
            var folder = Path.GetDirectoryName(guiContext.FileName);
            var filePath = entry.GetFullPath();

            var toolsAssetInfo = guiContext.GetOrLoadToolsAssetInfo();
            ValveResourceFormat.ToolsAssetInfo.ToolsAssetInfo.File? assetInfo = null;

            if (toolsAssetInfo != null && !toolsAssetInfo.Files.TryGetValue(filePath, out assetInfo))
            {
                var gameRootPath = string.Concat(Path.GetFileName(folder), "/", filePath);

                foreach (var (filePathTemp, assetInfoTemp) in toolsAssetInfo.Files)
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
            if (assetInfo == null && toolsAssetInfo != null && filePath.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var filePathUncompiled = filePath[..^2];

                if (!toolsAssetInfo.Files.TryGetValue(filePathUncompiled, out assetInfo))
                {
                    var gameRootPath = string.Concat(Path.GetFileName(folder), "/", filePathUncompiled);

                    foreach (var (filePathTemp, assetInfoTemp) in toolsAssetInfo.Files)
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

            if (assetInfo == null || toolsAssetInfo == null)
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

            var referencedBy = new Dictionary<string, ResourceReferenceKind>(StringComparer.OrdinalIgnoreCase);

            void AddReferencedBy(string file, ResourceReferenceKind kind)
            {
                referencedBy.TryGetValue(file, out var kinds);
                referencedBy[file] = kinds | kind;
            }

            foreach (var (filePathTemp, assetInfoTemp) in toolsAssetInfo.Files)
            {
                if (assetInfoTemp.ChildResources.Contains(filePath))
                {
                    AddReferencedBy(filePathTemp, ResourceReferenceKind.Child);
                }

                if (assetInfoTemp.ExternalReferences.Contains(filePath))
                {
                    AddReferencedBy(filePathTemp, ResourceReferenceKind.External);
                }

                if (assetInfoTemp.WeakReferences.Contains(filePath))
                {
                    AddReferencedBy(filePathTemp, ResourceReferenceKind.Weak);
                }

                if (assetInfoTemp.AdditionalRelatedFiles.Contains(filePath))
                {
                    AddReferencedBy(filePathTemp, ResourceReferenceKind.Related);
                }

                if (assetInfoTemp.InputDependencies.Exists(f => f.Filename == filePath)
                    || assetInfoTemp.AdditionalInputDependencies.Exists(f => f.Filename == filePath)
                    || assetInfoTemp.SpecialInputDependencies.Exists(f => f.Filename == filePath))
                {
                    AddReferencedBy(filePathTemp, ResourceReferenceKind.InputDependency);
                }
            }

            var references = referencedBy
                .Select(static reference => ResourceReference.Create(reference.Key, reference.Value))
                .ToList();
            leftSplitter.Panel2.Controls.Add(new ResourceReferenceList(guiContext, references));

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

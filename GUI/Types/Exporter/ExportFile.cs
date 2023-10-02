using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static void ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var stream = AdvancedGuiFileLoader.GetPackageEntryStream(vrfGuiContext.CurrentPackage, file);

            ExtractFileFromStream(file.GetFileName(), stream, vrfGuiContext, decompile);
        }

        public static void ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (decompile && fileName.EndsWith("_c", StringComparison.Ordinal))
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(null, vrfGuiContext),
                };

                var resource = new Resource
                {
                    FileName = fileName,
                };
                resource.Read(stream);

                var extension = FileExtract.GetExtension(resource);

                if (extension == null)
                {
                    stream.Dispose();
                    Log.Error(nameof(ExportFile), $"Export for \"{fileName}\" has no suitable extension");
                    return;
                }

                var filter = $"{extension} file|*.{extension}";

                if (GltfModelExporter.CanExport(resource))
                {
                    var gltfFilter = "glTF|*.gltf";
                    var glbFilter = "glTF Binary|*.glb";

                    filter = $"{gltfFilter}|{glbFilter}|{filter}";
                }

                using var dialog = new SaveFileDialog
                {
                    FileName = Path.GetFileNameWithoutExtension(fileName),
                    InitialDirectory = Settings.Config.SaveDirectory,
                    DefaultExt = extension,
                    Filter = filter,
                };

                var result = dialog.ShowDialog();

                if (result != DialogResult.OK)
                {
                    stream.Dispose();
                    return;
                }

                var directory = Path.GetDirectoryName(dialog.FileName);
                Settings.Config.SaveDirectory = directory;

                var extractDialog = new ExtractProgressForm(exportData, directory, true)
                {
                    ShownCallback = (form, cancellationToken) =>
                    {
                        form.SetProgress($"Extracting {fileName} to \"{Path.GetFileName(dialog.FileName)}\"");

                        Task.Run(async () =>
                        {
                            await form.ExtractFile(resource, fileName, dialog.FileName, true).ConfigureAwait(false);
                        }, cancellationToken).ContinueWith(t =>
                        {
                            stream.Dispose();
                            resource.Dispose();

                            form.ExportContinueWith(t);
                        }, CancellationToken.None);
                    }
                };
                extractDialog.ShowDialog();
            }
            else
            {
                var dialog = new SaveFileDialog
                {
                    InitialDirectory = Settings.Config.SaveDirectory,
                    Filter = "All files (*.*)|*.*",
                    FileName = fileName,
                };
                var userOK = dialog.ShowDialog();

                if (userOK == DialogResult.OK)
                {
                    Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);

                    Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(dialog.FileName)}\"");

                    using var streamOutput = dialog.OpenFile();
                    stream.CopyTo(streamOutput);
                }

                stream.Dispose();
            }
        }

        public static void ExtractFilesFromTreeNode(BetterTreeNode selectedNode, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (!selectedNode.IsFolder)
            {
                var file = selectedNode.PackageEntry;
                // We are a file
                ExtractFileFromPackageEntry(file, vrfGuiContext, decompile);
            }
            else
            {
                // We are a folder
                using var dialog = new FolderBrowserDialog
                {
                    InitialDirectory = Settings.Config.SaveDirectory,
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                Settings.Config.SaveDirectory = dialog.SelectedPath;

                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var extractDialog = new ExtractProgressForm(exportData, dialog.SelectedPath, decompile);
                extractDialog.QueueFiles(selectedNode);
                extractDialog.ShowDialog();
            }
        }

        public static void ExtractFilesFromListViewNodes(BetterListView.SelectedListViewItemCollection items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            using var dialog = new FolderBrowserDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            Settings.Config.SaveDirectory = dialog.SelectedPath;

            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var extractDialog = new ExtractProgressForm(exportData, dialog.SelectedPath, decompile);

            // When queuing files this way, it'll preserve the original tree
            // which is probably unwanted behaviour? It works tho /shrug
            foreach (ListViewItem item in items)
            {
                extractDialog.QueueFiles((BetterTreeNode)item.Tag);
            }

            extractDialog.ShowDialog();
        }
    }
}

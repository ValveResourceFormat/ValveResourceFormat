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
                    const string gltfFilter = "glTF|*.gltf";
                    const string glbFilter = "glTF Binary|*.glb";

                    filter = $"{filter}|{gltfFilter}|{glbFilter}";
                }

                using var dialog = new SaveFileDialog
                {
                    Title = "Choose where to save the file",
                    FileName = Path.GetFileNameWithoutExtension(fileName),
                    InitialDirectory = Settings.Config.SaveDirectory,
                    DefaultExt = extension,
                    Filter = filter,
                    AddToRecent = true,
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
                using var dialog = new SaveFileDialog
                {
                    Title = "Choose where to save the file",
                    InitialDirectory = Settings.Config.SaveDirectory,
                    Filter = "All files (*.*)|*.*",
                    FileName = fileName,
                    AddToRecent = true,
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
                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var extractDialog = new ExtractProgressForm(exportData, null, decompile);
                extractDialog.QueueFiles(selectedNode);
                extractDialog.Execute();
            }
        }

        public static void ExtractFilesFromListViewNodes(BetterListView.SelectedListViewItemCollection items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var extractDialog = new ExtractProgressForm(exportData, null, decompile);

            // When queuing files this way, it'll preserve the original tree
            // which is probably unwanted behaviour? It works tho /shrug
            foreach (ListViewItem item in items)
            {
                extractDialog.QueueFiles((BetterTreeNode)item.Tag);
            }

            extractDialog.Execute();
        }
    }
}

using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static void ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile, ResourceOptions resourceFlags)
        {
            var stream = AdvancedGuiFileLoader.GetPackageEntryStream(vrfGuiContext.CurrentPackage, file);

            ExtractFileFromStream(file.GetFileName(), stream, vrfGuiContext, decompile, resourceFlags);
        }

        public static void ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile, ResourceOptions resourceFlags)
        {
            if (Path.GetExtension(fileName) == ".vmap_c")
            {
                fileName = MapExtract.AddSuffixToVmapName(fileName);
            }

            if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(null, vrfGuiContext),
                };

                var resourceTemp = new Resource
                {
                    FileName = fileName,
                };
                var resource = resourceTemp;
                string filaNameToSave;

                try
                {
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
                        return;
                    }

                    filaNameToSave = dialog.FileName;
                    resourceTemp = null;
                }
                finally
                {
                    resourceTemp?.Dispose();
                }

                var directory = Path.GetDirectoryName(filaNameToSave);
                Settings.Config.SaveDirectory = directory;

                var extractDialog = new ExtractProgressForm(exportData, directory, true)
                {
                    ShownCallback = (form, cancellationToken) =>
                    {
                        form.SetProgress($"Extracting {fileName} to \"{Path.GetFileName(filaNameToSave)}\"");

                        Task.Run(async () =>
                        {
                            await form.ExtractFile(resource, fileName, filaNameToSave, resourceFlags, true).ConfigureAwait(false);
                        }, cancellationToken).ContinueWith(t =>
                        {
                            stream.Dispose();
                            resource.Dispose();

                            form.ExportContinueWith(t);
                        }, CancellationToken.None);
                    }
                };

                try
                {
                    extractDialog.ShowDialog();
                    extractDialog = null;
                }
                finally
                {
                    extractDialog?.Dispose();
                    exportData.VrfGuiContext.Dispose();
                }
            }
            else
            {
                if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out var content))
                {
                    var extension = Path.GetExtension(content.FileName);
                    fileName = Path.ChangeExtension(fileName, extension);
                    stream.Dispose();

                    stream = new MemoryStream(content.Data);
                    content.Dispose();
                }

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

        public static void ExtractFilesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (!selectedNode.IsFolder)
            {
                // We are a file
                var file = selectedNode.PackageEntry;

                if (TryOpenCustomFileExportDialogue(file.TypeName, out ResourceOptions resourceOptions))
                {
                    ExtractFileFromPackageEntry(file, vrfGuiContext, decompile, resourceOptions);
                }
                else
                {
                    return;
                }
            }
            else
            {
                // We are a folder
                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var extractDialog = new ExtractProgressForm(exportData, null, decompile);

                try
                {
                    extractDialog.QueueFiles(selectedNode);
                    extractDialog.Execute();
                    extractDialog = null;
                }
                finally
                {
                    extractDialog?.Dispose();
                }
            }
        }

        public static void ExtractFilesFromListViewNodes(BetterListView.SelectedListViewItemCollection items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var extractDialog = new ExtractProgressForm(exportData, null, decompile);

            try
            {
                // When queuing files this way, it'll preserve the original tree
                // which is probably unwanted behaviour? It works tho /shrug
                foreach (IBetterBaseItem item in items)
                {
                    extractDialog.QueueFiles(item);
                }

                extractDialog.Execute();
                extractDialog = null;
            }
            finally
            {
                extractDialog?.Dispose();
            }
        }

        public static bool TryOpenCustomFileExportDialogue(string fileTypeName, out ResourceOptions resourceOptions)
        {
            resourceOptions = new ResourceOptions();

            if (fileTypeName == "vmap_c")
            {
                var extractDialog = new VmapExport();

                try
                {
                    if (extractDialog.ShowVmapExportDialog() != DialogResult.Continue)
                    {
                        return false;
                    }

                    resourceOptions.VmapOptions = extractDialog.VmapExportFlags();

                    extractDialog?.Dispose();
                }
                finally
                {
                    extractDialog?.Dispose();
                }
            }

            return true;
        }
    }
}

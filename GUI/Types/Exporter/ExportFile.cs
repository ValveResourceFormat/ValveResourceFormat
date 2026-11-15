using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        public static async Task ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var currentPackage = vrfGuiContext.CurrentPackage;
            if (currentPackage == null)
            {
                ErrorReport.ShowError(new InvalidOperationException("The package is not available, cannot extract file"), file.GetFullPath());
                return;
            }

            var fileName = file.GetFullPath();

            var errorContext = vrfGuiContext.GetEntryDisplayPath(fileName);

            Stream stream;

            try
            {
                stream = currentPackage.GetMemoryMappedStreamIfPossible(file);
            }
            catch (Exception e)
            {
                ErrorReport.ShowError(e, errorContext);
                return;
            }

            await ExtractFileFromStream(fileName, stream, vrfGuiContext, decompile, errorContext).ConfigureAwait(true);
        }

        /// <param name="errorContext">Which file this is, shown when extracting it fails.</param>
        public static async Task ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile, string errorContext)
        {
            try
            {
                if (decompile && !await PreExportDisclaimer(Path.GetExtension(fileName)).ConfigureAwait(true))
                {
                    return;
                }

                if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    await DecompileResource(fileName, stream, vrfGuiContext).ConfigureAwait(true);
                }
                else
                {
                    await SaveFile(fileName, stream, decompile).ConfigureAwait(true);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ErrorReport.ShowError(e, errorContext);
            }
            finally
            {
                await stream.DisposeAsync().ConfigureAwait(true);
            }
        }

        private static async Task DecompileResource(string fileName, Stream stream, VrfGuiContext vrfGuiContext)
        {
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
                    await AppMessageDialogs.ShowMessageAsync(Locale.Export_NotSupported_Message(fileName), Locale.Export_FailedToExport_Title, MessageIcon.Warning).ConfigureAwait(true);
                    return;
                }

                var filter = GltfModelExporter.CanExport(resource)
                    ? Locale.Export_GltfFileFilter(extension)
                    : Locale.Export_FileFilter(extension);

                var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

                if (Path.GetExtension(fileName) == ".vmap_c")
                {
                    // When exporting a vmap, suggest saving with a suffix like de_dust2_d,
                    // to reduce conflicts when users end up recompiling the map with the same name as it exists in the game
                    fileNameForSave += "_d";
                }

                var pickedFileName = AppFileDialogs.SaveFile(Locale.Export_SaveFile_Title, fileNameForSave, extension, filter);

                if (pickedFileName == null)
                {
                    return;
                }

                filaNameToSave = pickedFileName;
                resourceTemp = null;
            }
            finally
            {
                resourceTemp?.Dispose();
            }

            var directory = Path.GetDirectoryName(filaNameToSave);
            // A tab of the file itself already has a context for it
            var ownsContext = vrfGuiContext.IsPackage;
            var exportData = new ExportData
            {
                VrfGuiContext = ownsContext ? new VrfGuiContext(fileName, vrfGuiContext) : vrfGuiContext,
                BypassResourceCache = !ownsContext,
            };

            try
            {
                var exporter = new PackageExporter(exportData, directory ?? string.Empty, true);

                // Cancelling closes the dialog while the extract is still winding down, so wait for it to
                // stop before the finally pulls the resource out from under it
                await exporter.ExecuteSingleFileExtractAsync(resource, fileName, filaNameToSave, Locale.Export_Extracting(fileName, Path.GetFileName(filaNameToSave))).ConfigureAwait(true);
            }
            finally
            {
                resource.Dispose();

                if (ownsContext)
                {
                    exportData.VrfGuiContext.Dispose();
                }
            }
        }

        private static async Task SaveFile(string fileName, Stream stream, bool decompile)
        {
            ContentFile? content = null;

            try
            {
                if (decompile && FileExtract.TryExtractNonResource(stream, fileName, out content))
                {
                    if (content.Data == null)
                    {
                        // Content has no data to extract, only potentially subfiles
                        Log.Info(nameof(ExportFile), $"File \"{fileName}\" has no extractable data");
                        return;
                    }

                    fileName = Path.ChangeExtension(fileName, Path.GetExtension(content.FileName));
                    stream = new MemoryStream(content.Data);
                }

                var saveFileName = AppFileDialogs.SaveFile(Locale.Export_SaveFile_Title, Path.GetFileName(fileName), null, Locale.Dialog_AllFilesFileFilter);

                if (saveFileName == null)
                {
                    return;
                }

                using var streamOutput = File.Create(saveFileName);
                await stream.CopyToAsync(streamOutput).ConfigureAwait(true);

                Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(saveFileName)}\"");
            }
            finally
            {
                content?.Dispose();
            }
        }

        public static async Task ExtractFilesFromTreeNode(IBetterBaseItem selectedNode, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (!selectedNode.IsFolder)
            {
                var file = selectedNode.PackageEntry;
                Debug.Assert(file != null);
                await ExtractFileFromPackageEntry(file, vrfGuiContext, decompile).ConfigureAwait(true);
            }
            else
            {
                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var exporter = new PackageExporter(exportData, null, decompile);
                exporter.QueueFiles(selectedNode);
                exporter.ExecuteMultipleFileExtract();
            }
        }

        public static void ExtractFilesFromListViewNodes(List<ListViewItem> items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var exportData = new ExportData
            {
                VrfGuiContext = vrfGuiContext,
            };

            var exporter = new PackageExporter(exportData, null, decompile);

            // When queuing files this way, it'll preserve the original tree
            // which is probably unwanted behaviour? It works tho /shrug
            foreach (var item in items.Cast<IBetterBaseItem>())
            {
                exporter.QueueFiles(item);
            }

            exporter.ExecuteMultipleFileExtract();
        }

        public static async Task<bool> PreExportDisclaimer(string fileExtension)
        {
            var messageString = "";

            switch (fileExtension)
            {
                case ".vmap_c":

                    messageString = Locale.Export_DecompileWarning_Message;
                    break;

                default:
                    break;
            }

            if (!string.IsNullOrEmpty(messageString))
            {
                return await AppMessageDialogs.ConfirmAsync(messageString, Locale.Export_DecompileWarning_Title, MessageIcon.Warning).ConfigureAwait(true);
            }

            return true;
        }
    }
}

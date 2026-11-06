using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    static class ExportFile
    {
        /// <summary>
        /// Saves the file that <paramref name="exportData"/> describes, decompiled or as it is.
        /// </summary>
        public static Task ExtractFile(ExportData exportData, bool decompile)
            => RunWithFile(exportData, (fileName, stream) => ExtractFileFromStream(fileName, stream, exportData.VrfGuiContext, decompile));

        /// <summary>
        /// Checks whether the file is a Source 1 or GoldSrc file that can be exported with its game files.
        /// </summary>
        public static bool CanExportWithDependencies(ExportData exportData)
        {
            using var stream = OpenStream(exportData);

            return stream != null && ConvertedExport.GetFormat(GetFileName(exportData), stream).IsSupported;
        }

        /// <summary>
        /// Exports a Source 1 or GoldSrc file together with the files of its game that converting it opens.
        /// </summary>
        public static Task ExportWithDependencies(ExportData exportData)
            => RunWithFile(exportData, (fileName, stream) => GameFilesExport.ExportWithDependencies(fileName, stream, exportData.VrfGuiContext));

        // The path of the file inside of its package, or its name when it is on disk
        private static string GetFileName(ExportData exportData)
            => exportData.PackageEntry?.GetFullPath() ?? Path.GetFileName(exportData.VrfGuiContext.FileName);

        private static Stream? OpenStream(ExportData exportData)
        {
            var vrfGuiContext = exportData.VrfGuiContext;

            if (exportData.PackageEntry != null)
            {
                return vrfGuiContext.CurrentPackage?.GetMemoryMappedStreamIfPossible(exportData.PackageEntry);
            }

            if (!File.Exists(vrfGuiContext.FileName))
            {
                return null;
            }

            return File.Open(vrfGuiContext.FileName, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
        }

        // Opens the file, shows what fails, and disposes the stream when the work is done
        private static async Task RunWithFile(ExportData exportData, Func<string, Stream, Task> work)
        {
            var fileName = GetFileName(exportData);
            var errorContext = exportData.VrfGuiContext.GetEntryDisplayPath(fileName);
            Stream? stream;

            try
            {
#pragma warning disable CA2000 // Disposed after the work below
                stream = OpenStream(exportData);
#pragma warning restore CA2000
            }
            catch (Exception e)
            {
                ErrorReport.ShowError(e, errorContext);
                return;
            }

            if (stream == null)
            {
                ErrorReport.ShowError(new InvalidOperationException("The file is not available"), errorContext);
                return;
            }

            try
            {
                await work(fileName, stream).ConfigureAwait(true);
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

        private static async Task ExtractFileFromStream(string fileName, Stream stream, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (decompile && !await PreExportDisclaimer(Path.GetExtension(fileName)).ConfigureAwait(true))
            {
                return;
            }

            if (decompile && fileName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
            {
                await DecompileResource(fileName, stream, vrfGuiContext).ConfigureAwait(true);
                return;
            }

            if (decompile)
            {
                var format = ConvertedExport.GetFormat(fileName, stream);

                if (format.IsSupported)
                {
                    // The conversion of a map takes a while
                    using var converted = await Task.Run(() => ConvertedExport.Convert(fileName, stream, format, vrfGuiContext)).ConfigureAwait(true);

                    var exportData = new ExportData
                    {
                        VrfGuiContext = converted.Context,
                    };

                    await ExportResource(fileName, converted.Resource, exportData).ConfigureAwait(true);
                    return;
                }
            }

            await SaveFile(fileName, stream, decompile).ConfigureAwait(true);
        }

        private static async Task DecompileResource(string fileName, Stream stream, VrfGuiContext vrfGuiContext)
        {
            using var resource = new Resource
            {
                FileName = fileName,
            };

            resource.Read(stream);

            // A tab of the file itself already has a context for it
            var ownsContext = vrfGuiContext.IsPackage;
            var exportData = new ExportData
            {
                VrfGuiContext = ownsContext ? new VrfGuiContext(fileName, vrfGuiContext) : vrfGuiContext,
                BypassResourceCache = !ownsContext,
            };

            try
            {
                await ExportResource(fileName, resource, exportData).ConfigureAwait(true);
            }
            finally
            {
                if (ownsContext)
                {
                    exportData.VrfGuiContext.Dispose();
                }
            }
        }

        private static async Task ExportResource(string fileName, Resource resource, ExportData exportData)
        {
            var extension = FileExtract.GetExtension(resource);

            if (extension == null)
            {
                await AppMessageDialogs.ShowMessageAsync($"Exporting \"{fileName}\" is not supported.", "Failed to export", MessageIcon.Warning).ConfigureAwait(true);
                return;
            }

            var filter = $"{extension} file|*.{extension}";

            if (GltfModelExporter.CanExport(resource))
            {
                const string gltfFilter = "glTF|*.gltf";
                const string glbFilter = "glTF Binary|*.glb";

                filter = $"{filter}|{gltfFilter}|{glbFilter}";
            }

            var fileNameForSave = Path.GetFileNameWithoutExtension(fileName);

            if (resource.ResourceType == ResourceType.Map)
            {
                // When exporting a vmap, suggest saving with a suffix like de_dust2_d,
                // to reduce conflicts when users end up recompiling the map with the same name as it exists in the game
                fileNameForSave += "_d";
            }

            var pickedFileName = AppFileDialogs.SaveFile("Choose where to save the file", fileNameForSave, extension, filter);

            if (pickedFileName == null)
            {
                return;
            }

            var directory = Path.GetDirectoryName(pickedFileName);
            var exporter = new PackageExporter(exportData, directory ?? string.Empty, true);

            // Cancelling closes the dialog while the extract is still winding down, so wait for it to
            // stop before the caller pulls the resource out from under it
            await exporter.ExecuteSingleFileExtractAsync(resource, fileName, pickedFileName, $"Extracting {fileName} to \"{Path.GetFileName(pickedFileName)}\"").ConfigureAwait(true);
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

                var saveFileName = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileName(fileName), null, "All files (*.*)|*.*");

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

                var fileData = new ExportData
                {
                    PackageEntry = file,
                    VrfGuiContext = vrfGuiContext,
                };

                await ExtractFile(fileData, decompile).ConfigureAwait(true);
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

                    messageString =
                    """
                    Decompiling Source2 maps is a difficult process, as such the output will be messy and imperfect, and will not resemble how
                    real .vmap files are made!

                    - Models will be merged by material across the map.
                    - Parts of the skybox mesh might be missing.
                    - The collision of the map will be merged into one mesh using special materials.
                    - The map will lack lightmap resolution volumes.
                    - Hammer meshes will be triangulated.

                    It is NOT ADVISED to work on decompiled maps as your first map if you are new to mapping!
                    """;
                    break;

                default:
                    break;
            }

            if (!string.IsNullOrEmpty(messageString))
            {
                return await AppMessageDialogs.ConfirmAsync(messageString, "Decompile warning", MessageIcon.Warning).ConfigureAwait(true);
            }

            return true;
        }
    }
}

using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.ExceptionServices;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat.IO;

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

            // The context is either the package, or a tab opened from it which already names the file
            var errorContext = vrfGuiContext.FileName == fileName ? vrfGuiContext.DisplayPath : $"{fileName} ← {vrfGuiContext.DisplayPath}";

            await ExtractFile(fileName, () => GameFileLoader.GetPackageEntryStream(currentPackage, file), file.CRC32 > 0 ? file.CRC32 : null,
                ContentExportSource.FromPackage(currentPackage), vrfGuiContext, decompile, errorContext).ConfigureAwait(true);
        }

        public static async Task ExtractFileFromDisk(string path, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var contentPath = ContentPaths.GetContentPathFromDiskOrFileName(path, decompile ? message => Log.Warn(nameof(ExportFile), message) : null);

            await ExtractFile(contentPath, () => File.OpenRead(path), null, ContentExportSource.FromDiskFile(path), vrfGuiContext, decompile, vrfGuiContext.DisplayPath).ConfigureAwait(true);
        }

        /// <summary>
        /// Decompiles or saves a single file.
        /// </summary>
        /// <param name="fileName">Content path of the file.</param>
        /// <param name="open">Opens the file's contents.</param>
        /// <param name="crc32">The CRC32 the file's contents must have, when it is known.</param>
        /// <param name="source">Where the file came from, the dependencies it is decompiled with come from there.</param>
        /// <param name="vrfGuiContext">The context the file was opened in.</param>
        /// <param name="decompile">Whether to decompile the file instead of saving it as is.</param>
        /// <param name="errorContext">Which file this is, shown when extracting it fails.</param>
        private static async Task ExtractFile(string fileName, Func<Stream> open, uint? crc32, ContentExportSource? source, VrfGuiContext vrfGuiContext, bool decompile, string errorContext)
        {
            try
            {
                if (decompile && !await PreExportDisclaimer(Path.GetExtension(fileName)).ConfigureAwait(true))
                {
                    return;
                }

                if (decompile && ContentPaths.HasCompiledSuffix(fileName))
                {
                    using var fileContext = new VrfGuiContext(fileName, vrfGuiContext);
                    var exporter = new PackageExporter(new ExportData { VrfGuiContext = fileContext }, decompile: true);
                    await exporter.ExecuteSingleFileExtractAsync(fileName, open, source).ConfigureAwait(true);
                }
                else if (decompile && CanExtractNonResource(fileName, open))
                {
                    var exporter = new PackageExporter(new ExportData { VrfGuiContext = vrfGuiContext }, decompile: true);
                    await exporter.ExecuteSingleNonResourceExtractAsync(fileName, open).ConfigureAwait(true);
                }
                else
                {
                    await SaveFile(fileName, open, crc32).ConfigureAwait(true);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ErrorReport.ShowError(e, errorContext);
            }
        }

        private static bool CanExtractNonResource(string fileName, Func<Stream> open)
        {
            using var stream = open();
            using var content = FileExtract.ExtractNonResource(stream, fileName);

            return content != null;
        }

        private static async Task SaveFile(string fileName, Func<Stream> open, uint? crc32)
        {
            var saveFileName = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileName(fileName), null, "All files (*.*)|*.*");

            if (saveFileName == null)
            {
                return;
            }

            Exception? failure = null;

            var exporter = new ContentExporter(Path.GetDirectoryName(saveFileName)!)
            {
                FileFailed = (_, exception) => failure = exception,
            };

            var item = new ContentExportItem
            {
                ContentPath = fileName,
                Open = open,
                Crc32 = crc32,
                Output = ContentExportOutput.Raw,
                TargetFile = saveFileName,
            };

            await Task.Run(() => exporter.Export([item])).ConfigureAwait(true);

            if (failure != null)
            {
                ExceptionDispatchInfo.Throw(failure);
            }

            Log.Info(nameof(ExportFile), $"Saved \"{Path.GetFileName(saveFileName)}\"");
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

                var exporter = new PackageExporter(exportData, decompile);
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

            var exporter = new PackageExporter(exportData, decompile);

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

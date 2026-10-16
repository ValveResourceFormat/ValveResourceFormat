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

            await ExtractFile(new ContentExportItem(currentPackage, file), vrfGuiContext, decompile, errorContext).ConfigureAwait(true);
        }

        public static async Task ExtractFileFromDisk(string path, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var item = new ContentExportItem(path, decompile ? message => Log.Warn(nameof(ExportFile), message) : null);

            await ExtractFile(item, vrfGuiContext, decompile, vrfGuiContext.DisplayPath).ConfigureAwait(true);
        }

        /// <summary>
        /// Decompiles a single file, or saves it as is when it is not decompiled or there is nothing to decompile.
        /// </summary>
        /// <param name="item">The file.</param>
        /// <param name="vrfGuiContext">The context the file was opened in.</param>
        /// <param name="decompile">Whether to decompile the file instead of saving it as is.</param>
        /// <param name="errorContext">Which file this is, shown when extracting it fails.</param>
        private static async Task ExtractFile(ContentExportItem item, VrfGuiContext vrfGuiContext, bool decompile, string errorContext)
        {
            try
            {
                if (decompile && !await PreExportDisclaimer(Path.GetExtension(item.ContentPath)).ConfigureAwait(true))
                {
                    return;
                }

                if (decompile && item.CanDecompile())
                {
                    using var fileContext = new VrfGuiContext(item.ContentPath, vrfGuiContext);
                    var exporter = new PackageExporter(fileContext, decompile: true);
                    await exporter.ExecuteSingleFileExtractAsync(item).ConfigureAwait(true);
                }
                else
                {
                    await SaveFile(item).ConfigureAwait(true);
                }
            }
            catch (Exception e) when (e is not OperationCanceledException)
            {
                ErrorReport.ShowError(e, errorContext);
            }
        }

        private static async Task SaveFile(ContentExportItem item)
        {
            var saveFileName = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileName(item.ContentPath), null, "All files (*.*)|*.*");

            if (saveFileName == null)
            {
                return;
            }

            Exception? failure = null;

            var exporter = new ContentExporter(Path.GetDirectoryName(saveFileName)!)
            {
                FileFailed = (_, exception) => failure = exception,
            };

            var rawItem = new ContentExportItem(item)
            {
                Output = ContentExportOutput.Raw,
                TargetFile = saveFileName,
            };

            await Task.Run(() => exporter.Export([rawItem])).ConfigureAwait(true);

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
                var exporter = new PackageExporter(vrfGuiContext, decompile);
                exporter.QueueFiles(selectedNode);
                await exporter.ExecuteMultipleFileExtractAsync().ConfigureAwait(true);
            }
        }

        public static async Task ExtractFilesFromListViewNodes(List<ListViewItem> items, VrfGuiContext vrfGuiContext, bool decompile)
        {
            var exporter = new PackageExporter(vrfGuiContext, decompile);

            foreach (var item in items.Cast<IBetterBaseItem>())
            {
                exporter.QueueFiles(item);
            }

            await exporter.ExecuteMultipleFileExtractAsync().ConfigureAwait(true);
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

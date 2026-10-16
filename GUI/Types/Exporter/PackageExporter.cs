using System.Diagnostics;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Types.Exporter
{
    /// <summary>
    /// Extracts or decompiles package entries to a folder while reporting progress to a <see cref="GenericProgressForm"/>.
    /// </summary>
    class PackageExporter
    {
        private class FileTypeToExtract
        {
            public string? OutputFormat;
            public int Count = 1;
        }

        private const string DoNotExport = "* Do not export *";

        private readonly bool decompile;
        private readonly VrfGuiContext guiContext;
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
        private readonly List<PackageEntry> filesToExtract = [];
        private readonly Lock failureLock = new();
        private int processed;
        private ContentExporter? exporter;
        private (string FileName, Exception Exception)? firstFailure;
        private bool includeDependencies;
        private GenericProgressForm? progress;

        private int FailedFileCount => exporter?.FailedFileCount ?? 0;

        public PackageExporter(VrfGuiContext guiContext, bool decompile)
        {
            this.guiContext = guiContext;
            this.decompile = decompile;
        }

        public void QueueFiles(IBetterBaseItem root)
        {
            if (root.IsFolder)
            {
                if (root.PkgNode != null)
                {
                    QueueFiles(root.PkgNode);
                }
            }
            else if (root.PackageEntry != null)
            {
                QueueFiles(root.PackageEntry);
            }
        }

        public void QueueFiles(VirtualPackageNode root)
        {
            foreach (var node in root.Folders)
            {
                QueueFiles(node.Value);
            }

            foreach (var file in root.Files)
            {
                QueueFiles(file);
            }
        }

        public void QueueFiles(PackageEntry file)
        {
            if (fileTypesToExtract.TryGetValue(file.TypeName, out var fileType))
            {
                fileType.Count++;
            }
            else
            {
                fileTypesToExtract[file.TypeName] = new FileTypeToExtract(); // Type to be filled in later
            }

            filesToExtract.Add(file);
        }

        /// <summary>
        /// Asks for output types and a destination folder, then extracts all queued files behind a modal progress dialog.
        /// </summary>
        public async Task ExecuteMultipleFileExtractAsync()
        {
            if (filesToExtract.Count == 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync("There are no files to extract", "Failed to extract", MessageIcon.Warning);
                return;
            }

            if (decompile && ShowTypesDialog(canSkip: true) != DialogResult.Continue)
            {
                return;
            }

            if (decompile && fileTypesToExtract.TryGetValue("vmap_c", out var mapType) && mapType.OutputFormat != null
                && !await ExportFile.PreExportDisclaimer(".vmap_c").ConfigureAwait(true))
            {
                return;
            }

            var remember = decompile ? AppFileDialogs.RememberIn.DecompileDirectory : AppFileDialogs.RememberIn.SaveDirectory;
            var selectedPath = AppFileDialogs.PickFolder("Choose which folder to extract files to", remember);

            if (selectedPath == null)
            {
                return;
            }

            // Nothing outside the export owns the files it reads, so there is nothing to wait for here
            RunInDialog(cancellationToken => ExportQueuedFiles(selectedPath, cancellationToken), null, out _);
        }

        /// <summary>
        /// Asks for the output type of a file, when it has a choice, and where to write it, then exports it behind a
        /// modal progress dialog. glTF goes to a picked file, everything else under a picked root folder at its content
        /// path.
        /// </summary>
        /// <param name="item">The file, its output and loader are chosen here.</param>
        public Task ExecuteSingleFileExtractAsync(ContentExportItem item)
        {
            var contentPath = item.ContentPath;
            var typeName = Path.GetExtension(contentPath).TrimStart('.');
            fileTypesToExtract[typeName] = new FileTypeToExtract();

            if ((ContentPaths.HasCompiledSuffix(contentPath) || ContentExporter.CanExportGltf(contentPath))
                && ShowTypesDialog(canSkip: false) != DialogResult.Continue)
            {
                return Task.CompletedTask;
            }

            var outputFormat = fileTypesToExtract[typeName].OutputFormat;
            var output = GetOutput(outputFormat);
            string outputRoot;
            string? gltfPath = null;

            if (output is ContentExportOutput.Gltf or ContentExportOutput.Glb)
            {
                var filter = output == ContentExportOutput.Gltf ? "glTF|*.gltf" : "glTF Binary|*.glb";
                gltfPath = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileNameWithoutExtension(contentPath), outputFormat, filter);

                if (gltfPath == null)
                {
                    return Task.CompletedTask;
                }

                outputRoot = ContentPaths.ResolveOutputRoot(Path.GetDirectoryName(gltfPath)!, [contentPath]);
            }
            else
            {
                var folder = AppFileDialogs.PickFolder("Choose which folder to extract files to", AppFileDialogs.RememberIn.DecompileDirectory);

                if (folder == null)
                {
                    return Task.CompletedTask;
                }

                outputRoot = ContentPaths.ResolveOutputRoot(folder, [contentPath]);
            }

            var exportItem = new ContentExportItem(item)
            {
                FileLoader = guiContext.FileLoaderNoCache,
                Output = output,
                TargetFile = gltfPath,
            };

            var initialText = gltfPath == null ? $"Decompiling {contentPath}" : $"Exporting {contentPath} to \"{Path.GetFileName(gltfPath)}\"";

            RunInDialog(cancellationToken =>
            {
                var exporter = CreateContentExporter(outputRoot, confirmedFile: gltfPath);
                exporter.Export([exportItem], cancellationToken);
            }, initialText, out var workCompletion);

            return workCompletion;
        }

        private void RunInDialog(Action<CancellationToken> work, string? initialText, out Task workCompletion)
        {
            using var dialog = new GenericProgressForm { StayOpenOnCompletion = true };
            progress = dialog;

            if (initialText != null)
            {
                dialog.SetProgress(initialText);
            }

            dialog.OnProcess = cancellationToken =>
            {
                work(cancellationToken);

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(dialog.Elapsed)}";

                if (FailedFileCount > 0)
                {
                    var noun = FailedFileCount == 1 ? "file" : "files";
                    completedText += string.Create(CultureInfo.InvariantCulture, $", {FailedFileCount} {noun} failed");
                }

                Log.Info(nameof(PackageExporter), completedText);
                dialog.SetProgress(completedText);

                return Task.CompletedTask;
            };

            dialog.ShowDialog();

            // Cancelling closes the dialog right away while the work winds down, so the caller has to be handed
            // something to wait on before it disposes what the work is still reading from.
            workCompletion = AfterWorkStopped(dialog.WorkCompletion);
        }

        private async Task AfterWorkStopped(Task workCompletion)
        {
            await workCompletion.ConfigureAwait(true);

            if (FailedFileCount == 0)
            {
                return;
            }

            try
            {
                ShowFailureSummary();
            }
            catch (Exception e)
            {
                Log.Error(nameof(PackageExporter), $"Failed to show the export failure summary: {e}");
            }
        }

        private void ShowFailureSummary()
        {
            Debug.Assert(firstFailure != null);

            var (firstFailedFile, firstException) = firstFailure.Value;
            var failedFileCount = FailedFileCount;
            var noun = failedFileCount == 1 ? "file" : "files";
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"{failedFileCount} {noun} failed to export, the first one was:");
            text.AppendLine(firstFailedFile);
            text.AppendLine(firstException.Message);

            if (failedFileCount > 1)
            {
                var others = failedFileCount == 2 ? "failure is" : "failures are";
                text.AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"The other {others} listed in the console.");
            }

            // The context is either the package, or a file opened from it which already names the file
            var context = firstFailedFile == guiContext.FileName ? guiContext.DisplayPath : $"{firstFailedFile} ← {guiContext.DisplayPath}";

            var report = new StringBuilder();
            ErrorReport.AppendExceptionWithVersion(report, firstException, context);

            ErrorReport.ShowDialog("Some files failed to export", text.ToString().TrimEnd(), report.ToString());
        }

        private DialogResult ShowTypesDialog(bool canSkip)
        {
            using var typesDialog = new ExtractOutputTypesForm
            {
                IncludeDependencies = Settings.Config.DecompileDependencies,
            };
            typesDialog.ChangeTypeEvent += OnTypesDialogSelectedValueChanged;

            var hasVmap = fileTypesToExtract.ContainsKey("vmap_c");
            var rememberedTypes = Settings.Config.DecompileOutputTypes;

            bool IsSkippedForMap(string type) => hasVmap && type != "vmap_c";

            foreach (var type in fileTypesToExtract.OrderByDescending(x => x.Value.Count))
            {
                var fileName = "." + type.Key;
                var outputTypes = new List<string>
                {
                    ContentExporter.GetDecompiledExtension(fileName),
                };

                if (ContentExporter.CanExportGltf(fileName))
                {
                    outputTypes.Add("gltf");
                    outputTypes.Add("glb");
                }

                if (canSkip)
                {
                    outputTypes.Insert(0, DoNotExport);
                }

                // For folders containing a map, default to "do not export", except for the map itself.
                var selectedIndex = canSkip && !IsSkippedForMap(type.Key) ? 1 : 0;

                if (!IsSkippedForMap(type.Key) && rememberedTypes.TryGetValue(type.Key, out var rememberedType))
                {
                    var rememberedIndex = outputTypes.IndexOf(rememberedType.Length > 0 ? rememberedType : DoNotExport);

                    if (rememberedIndex >= 0)
                    {
                        selectedIndex = rememberedIndex;
                    }
                }

                type.Value.OutputFormat = outputTypes[selectedIndex] == DoNotExport ? null : outputTypes[selectedIndex];
                typesDialog.AddTypeToTable(type.Key, type.Value.Count, outputTypes, selectedIndex);
            }

            var result = typesDialog.ShowDialog();
            typesDialog.ChangeTypeEvent -= OnTypesDialogSelectedValueChanged;

            if (result != DialogResult.Continue)
            {
                return result;
            }

            includeDependencies = typesDialog.IncludeDependencies;
            Settings.Config.DecompileDependencies = includeDependencies;

            foreach (var (type, fileType) in fileTypesToExtract)
            {
                if (!IsSkippedForMap(type))
                {
                    rememberedTypes[type] = fileType.OutputFormat ?? string.Empty;
                }
            }

            return result;
        }

        private void OnTypesDialogSelectedValueChanged(object? sender, EventArgs e)
        {
            if (sender is not ComboBox control)
            {
                return;
            }

            if (control.Tag is not string type)
            {
                return;
            }

            var outputFormat = control.SelectedItem as string;
            fileTypesToExtract[type].OutputFormat = outputFormat == DoNotExport ? null : outputFormat;
        }

        private void ExportQueuedFiles(string folder, CancellationToken cancellationToken)
        {
            Debug.Assert(progress != null);

            var currentPackage = guiContext.CurrentPackage ?? throw new InvalidOperationException("The package is not available, cannot extract files");

            Log.Info(nameof(PackageExporter), $"Folder export started to \"{folder}\"");

            processed = 0;
            progress.SetBarMax(filesToExtract.Count);

            var fileLoader = guiContext.FileLoaderNoCache;
            var items = new List<ContentExportItem>(filesToExtract.Count);

            foreach (var packageFile in filesToExtract)
            {
                ContentExportOutput output;

                if (!decompile)
                {
                    output = ContentExportOutput.Raw;
                }
                else if (fileTypesToExtract[packageFile.TypeName].OutputFormat is { } outputFormat)
                {
                    output = GetOutput(outputFormat);
                }
                else
                {
                    processed++;
                    continue;
                }

                items.Add(new ContentExportItem(currentPackage, packageFile)
                {
                    FileLoader = fileLoader,
                    Output = output,
                });
            }

            var exporter = CreateContentExporter(ContentPaths.ResolveOutputRoot(folder, items.Select(static item => item.ContentPath)));

            progress.SetBarValue(processed);
            exporter.Export(items, cancellationToken);
        }

        private static ContentExportOutput GetOutput(string? outputFormat) => outputFormat switch
        {
            "gltf" => ContentExportOutput.Gltf,
            "glb" => ContentExportOutput.Glb,
            _ => ContentExportOutput.Decompile,
        };

        private static void LogSkipped(string path)
        {
            Log.Info(nameof(PackageExporter), $"Skipped \"{path}\", it already exists");
        }

        private void ReportFailure(string fileName, Exception exception)
        {
            lock (failureLock)
            {
                firstFailure ??= (fileName, exception);
            }

            Log.Error(nameof(PackageExporter), $"Failed to extract '{fileName}': {exception}");
        }

        /// <summary>
        /// Creates the exporter that writes the files of one export.
        /// </summary>
        /// <param name="outputRoot">The folder content paths are relative to.</param>
        /// <param name="confirmedFile">
        /// A file the user already chose to overwrite. When it is the first file of the export that already exists, every
        /// existing file is overwritten without asking.
        /// </param>
        private ContentExporter CreateContentExporter(string outputRoot, string? confirmedFile = null)
        {
            Debug.Assert(progress != null);

            Log.Info(nameof(PackageExporter), $"Writing files to \"{outputRoot}\"");

            var dialog = progress;

            return exporter = new ContentExporter(outputRoot)
            {
                IncludeDependencies = includeDependencies,
                Progress = dialog,
                ConfirmExistingFiles = path => path.Equals(confirmedFile, StringComparison.OrdinalIgnoreCase) ? ExistingFileAction.Overwrite : ConfirmExistingFiles(path),
                FileWritten = path => dialog.SetProgress($"+ {ContentPaths.GetRelativePath(outputRoot, path)}"),
                FileSkipped = LogSkipped,
                Warning = message => Log.Warn(nameof(PackageExporter), message),
                FileFailed = ReportFailure,
                ItemStarted = item => dialog.SetProgress(item.ContentPath),
                ItemCompleted = item =>
                {
                    if (!item.IsDependency)
                    {
                        dialog.SetBarValue(Interlocked.Increment(ref processed));
                    }
                },
            };
        }

        private ExistingFileAction ConfirmExistingFiles(string path)
        {
            Debug.Assert(progress != null);

            var dialog = progress;

            try
            {
                return dialog.Invoke(() =>
                {
                    var choice = AppMessageDialogs.Choose(
                        $"\"{path}\" already exists.\n\nOverwrite it and every other file of this export that already exists, or keep them?",
                        "Files already exist",
                        ["Overwrite all", "Skip existing"],
                        MessageIcon.Warning,
                        dialog);

                    if (choice < 0)
                    {
                        dialog.Close();
                        return ExistingFileAction.Cancel;
                    }

                    return choice == 0 ? ExistingFileAction.Overwrite : ExistingFileAction.Skip;
                });
            }
            catch (InvalidOperationException)
            {
                return ExistingFileAction.Cancel;
            }
        }
    }
}

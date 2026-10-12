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
        private readonly ExportData exportData;
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
        private readonly List<PackageEntry> filesToExtract = [];
        private readonly HashSet<string> gltfExportedFiles = new(StringComparer.OrdinalIgnoreCase);
        private string? path;
        private string? lastCreatedDirectory;
        private int processed;
        private int filesFailedToExport;
        private Exception? firstFailure;
        private string? firstFailedFile;
        private bool includeDependencies;
        private GenericProgressForm? progress;
        private GltfModelExporter? gltfExporter;

        private string OutputPath => path ?? throw new InvalidOperationException("Path must be set before extracting files");

        public PackageExporter(ExportData exportData, bool decompile)
        {
            this.exportData = exportData;
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
        public void ExecuteMultipleFileExtract()
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

            var remember = decompile ? AppFileDialogs.RememberIn.DecompileDirectory : AppFileDialogs.RememberIn.SaveDirectory;
            var selectedPath = AppFileDialogs.PickFolder("Choose which folder to extract files to", remember);

            if (selectedPath == null)
            {
                return;
            }

            path = selectedPath;

            // Nothing outside the export owns the files it reads, so there is nothing to wait for here
            if (decompile)
            {
                RunInDialog(DecompileQueuedFiles, null, out _);
            }
            else
            {
                RunInDialog(CopyQueuedFiles, null, out _);
            }
        }

        /// <summary>
        /// Asks for the output type of an already-read resource and where to write it, then decompiles it behind a modal
        /// progress dialog. glTF goes to a picked file, everything else under a picked root folder at its content path.
        /// </summary>
        /// <param name="resource">The resource, named by its content path.</param>
        /// <param name="source">Where the resource came from, the dependencies it is exported with come from there.</param>
        public Task ExecuteSingleFileExtractAsync(Resource resource, ContentExportSource? source)
        {
            var contentPath = resource.FileName ?? throw new ArgumentException("The resource has no file name.", nameof(resource));
            var typeName = Path.GetExtension(contentPath).TrimStart('.');
            fileTypesToExtract[typeName] = new FileTypeToExtract();

            if (ShowTypesDialog(canSkip: false) != DialogResult.Continue)
            {
                return Task.CompletedTask;
            }

            var outputFormat = fileTypesToExtract[typeName].OutputFormat;
            Task workCompletion;

            if (outputFormat is "gltf" or "glb")
            {
                var filter = outputFormat == "gltf" ? "glTF|*.gltf" : "glTF Binary|*.glb";
                var gltfPath = AppFileDialogs.SaveFile("Choose where to save the file", Path.GetFileNameWithoutExtension(contentPath), outputFormat, filter);

                if (gltfPath == null)
                {
                    return Task.CompletedTask;
                }

                RunInDialog(cancellationToken => ExportGltf(resource, gltfPath, null, cancellationToken), $"Exporting {contentPath} to \"{Path.GetFileName(gltfPath)}\"", out workCompletion);
                return workCompletion;
            }

            var folder = AppFileDialogs.PickFolder("Choose which folder to extract files to", AppFileDialogs.RememberIn.DecompileDirectory);

            if (folder == null)
            {
                return Task.CompletedTask;
            }

            var outputRoot = ContentPaths.ResolveOutputRoot(folder, ContentPaths.GetDirectoryName(contentPath));

            if (includeDependencies && source == null)
            {
                Log.Warn(nameof(PackageExporter), $"Dependencies of \"{contentPath}\" are not decompiled, it is not in an addon or mod folder.");
            }

            RunInDialog(cancellationToken =>
            {
                var exporter = CreateContentExporter(outputRoot);
                TryExport(contentPath, () => exporter.Export(resource, exportData.VrfGuiContext, source, cancellationToken: cancellationToken));
            }, $"Decompiling {contentPath}", out workCompletion);

            return workCompletion;
        }

        /// <summary>
        /// Asks for a root folder, then writes a file extracted from a non-resource file under it at its content path,
        /// behind a modal progress dialog.
        /// </summary>
        public Task ExecuteSingleFileExtractAsync(ContentFile contentFile)
        {
            var folder = AppFileDialogs.PickFolder("Choose which folder to extract files to", AppFileDialogs.RememberIn.DecompileDirectory);

            if (folder == null)
            {
                return Task.CompletedTask;
            }

            var outputRoot = ContentPaths.ResolveOutputRoot(folder, ContentPaths.GetDirectoryName(contentFile.FileName));

            RunInDialog(cancellationToken =>
            {
                var exporter = CreateContentExporter(outputRoot);
                TryExport(contentFile.FileName, () => exporter.Export(contentFile, cancellationToken: cancellationToken));
            }, $"Decompiling {contentFile.FileName}", out var workCompletion);

            return workCompletion;
        }

        private void RunInDialog(Action<CancellationToken> work, string? initialText, out Task workCompletion)
        {
            RunInDialog(cancellationToken =>
            {
                work(cancellationToken);
                return Task.CompletedTask;
            }, initialText, out workCompletion);
        }

        private void RunInDialog(Func<CancellationToken, Task> work, string? initialText, out Task workCompletion)
        {
            using var dialog = new GenericProgressForm { StayOpenOnCompletion = true };
            progress = dialog;

            if (initialText != null)
            {
                dialog.SetProgress(initialText);
            }

            if (decompile)
            {
                // We need to know what files were handled by the glTF exporter
                gltfExporter = new GltfModelExporter(new TrackingFileLoader(exportData.VrfGuiContext))
                {
                    ProgressReporter = dialog,
                };
            }

            dialog.OnProcess = async cancellationToken =>
            {
                await work(cancellationToken).ConfigureAwait(false);

                var completedText = $"Export completed in {GenericProgressForm.FormatTime(dialog.Elapsed)}";

                if (filesFailedToExport > 0)
                {
                    var noun = filesFailedToExport == 1 ? "file" : "files";
                    completedText += string.Create(CultureInfo.InvariantCulture, $", {filesFailedToExport} {noun} failed");
                }

                Log.Info(nameof(PackageExporter), completedText);
                dialog.SetProgress(completedText);
            };

            dialog.ShowDialog();

            // Cancelling closes the dialog right away while the work winds down, so the caller has to be handed
            // something to wait on before it disposes what the work is still reading from.
            workCompletion = AfterWorkStopped(dialog.WorkCompletion);
        }

        private async Task AfterWorkStopped(Task workCompletion)
        {
            await workCompletion.ConfigureAwait(true);

            if (filesFailedToExport == 0)
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
            Debug.Assert(firstFailure != null && firstFailedFile != null);

            var noun = filesFailedToExport == 1 ? "file" : "files";
            var text = new StringBuilder();
            text.AppendLine(CultureInfo.InvariantCulture, $"{filesFailedToExport} {noun} failed to export, the first one was:");
            text.AppendLine(firstFailedFile);
            text.AppendLine(firstFailure.Message);

            if (filesFailedToExport > 1)
            {
                var others = filesFailedToExport == 2 ? "failure is" : "failures are";
                text.AppendLine();
                text.AppendLine(CultureInfo.InvariantCulture, $"The other {others} listed in the console.");
            }

            // The context is either the package, or a file opened from it which already names the file
            var guiContext = exportData.VrfGuiContext;
            var context = firstFailedFile == guiContext.FileName ? guiContext.DisplayPath : $"{firstFailedFile} ← {guiContext.DisplayPath}";

            var report = new StringBuilder();
            ErrorReport.AppendExceptionWithVersion(report, firstFailure, context);

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
                var resourceType = ResourceTypeExtensions.DetermineByFileExtension("." + type.Key);
                var firstType = resourceType != ResourceType.Unknown
                    ? FileExtract.GetExtension(resourceType)
                    : ContentPaths.TrimCompiledSuffix(type.Key);

                var outputTypes = new List<string>()
                {
                    firstType,
                };

                if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod" or "vnmclip" or "vnmskel")
                {
                    outputTypes.Add("gltf");
                    outputTypes.Add("glb");
                }
                else if (firstType == "vtex")
                {
                    outputTypes.Insert(0, "image");
                }
                else if (firstType == "vsnd")
                {
                    outputTypes.Insert(0, "sound");
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

        /// <summary>
        /// Resets the progress for a folder export and gets the package its files are read from.
        /// </summary>
        /// <returns>The package, or null when there is none to read from.</returns>
        private Package? StartFolderExport()
        {
            Debug.Assert(progress != null);

            Log.Info(nameof(PackageExporter), $"Folder export started to \"{path}\"");

            processed = 0;
            progress.SetBarMax(filesToExtract.Count);

            var currentPackage = exportData.VrfGuiContext.CurrentPackage;

            if (currentPackage == null)
            {
                ReportFailure(exportData.VrfGuiContext.FileName, new InvalidOperationException("The package is not available, cannot extract files"));
            }

            return currentPackage;
        }

        private void CopyQueuedFiles(CancellationToken cancellationToken)
        {
            Debug.Assert(progress != null);

            if (StartFolderExport() is not { } currentPackage)
            {
                return;
            }

            var contentFolder = ContentPaths.GetCommonDirectory(filesToExtract.Select(static file => file.GetFullPath()));
            var exporter = CreateContentExporter(ContentPaths.ResolveOutputRoot(OutputPath, contentFolder));

            // Read the package in storage order to avoid seeking back and forth between entries
            var files = filesToExtract
                .OrderBy(static file => file.ArchiveIndex)
                .ThenBy(static file => file.Offset);

            foreach (var packageFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.SetBarValue(++processed);

                var fileFullName = packageFile.GetFullPath();
                progress.SetProgress(fileFullName);

                TryExport(fileFullName, () =>
                {
                    currentPackage.ReadEntry(packageFile, out var data, validateCrc: packageFile.CRC32 > 0);
                    exporter.WriteFile(fileFullName, data);
                });
            }
        }

        private void DecompileQueuedFiles(CancellationToken cancellationToken)
        {
            Debug.Assert(progress != null);

            var outputPath = OutputPath;

            if (StartFolderExport() is not { } currentPackage)
            {
                return;
            }

            var contentFolder = ContentPaths.GetCommonDirectory(filesToExtract.Select(static file => file.GetFullPath()));
            var exporter = CreateContentExporter(ContentPaths.ResolveOutputRoot(outputPath, contentFolder));
            var source = ContentExportSource.FromPackage(currentPackage);

            // Within each type, read the package in storage order to avoid seeking back and forth between entries.
            var files = filesToExtract
                .OrderBy(static file => ContentExporter.GetExportOrder(file.GetFullPath()))
                .ThenBy(static file => file.ArchiveIndex)
                .ThenBy(static file => file.Offset);

            foreach (var packageFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.SetBarValue(++processed);

                var outputFormat = fileTypesToExtract[packageFile.TypeName].OutputFormat;

                if (outputFormat == null)
                {
                    continue;
                }

                var fileFullName = packageFile.GetFullPath();
                var isGltf = outputFormat is "gltf" or "glb";

                if (exporter.IsHandled(fileFullName) || (isGltf && gltfExportedFiles.Contains(fileFullName)))
                {
                    continue;
                }

                progress.SetProgress(fileFullName);

                if (!ContentPaths.HasCompiledSuffix(packageFile.TypeName))
                {
                    TryExport(fileFullName, () =>
                    {
                        using var stream = GameFileLoader.GetPackageEntryStream(currentPackage, packageFile);
                        ExportNonResource(exporter, fileFullName, stream, cancellationToken);
                    });

                    continue;
                }

                using var resource = new Resource
                {
                    FileName = fileFullName,
                };

                try
                {
                    resource.Read(GameFileLoader.GetPackageEntryStream(currentPackage, packageFile));
                }
                catch (Exception e)
                {
                    ReportFailure(fileFullName, e);
                    continue;
                }

                if (isGltf)
                {
                    ExportGltf(resource, Path.ChangeExtension(exporter.GetOutputPath(fileFullName), outputFormat), exporter, cancellationToken);
                    continue;
                }

                TryExport(fileFullName, () => exporter.Export(resource, exportData.VrfGuiContext, source, cancellationToken: cancellationToken));
            }
        }

        private static void ExportNonResource(ContentExporter exporter, string contentPath, Stream stream, CancellationToken cancellationToken)
        {
            if (FileExtract.TryExtractNonResource(stream, contentPath, out var contentFile))
            {
                using (contentFile)
                {
                    exporter.Export(contentFile, cancellationToken: cancellationToken);
                }

                return;
            }

            var data = new byte[stream.Length - stream.Position];
            stream.ReadExactly(data);
            exporter.WriteFile(contentPath, data);
        }

        private void TryExport(string fileName, Action export)
        {
            try
            {
                export();
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ReportFailure(fileName, e);
            }
        }

        private static void LogSkipped(string path)
        {
            Log.Info(nameof(PackageExporter), $"Skipped \"{path}\", it already exists");
        }

        private void ReportFailure(string fileName, Exception exception)
        {
            filesFailedToExport++;

            if (firstFailure == null)
            {
                firstFailure = exception;
                firstFailedFile = fileName;
            }

            Log.Error(nameof(PackageExporter), $"Failed to extract '{fileName}': {exception}");
        }

        private void ExportGltf(Resource resource, string outFilePath, ContentExporter? exporter, CancellationToken cancellationToken)
        {
            Debug.Assert(gltfExporter != null);

            TryExport(resource.FileName ?? outFilePath, () =>
            {
                if (exporter != null && !exporter.CanWrite(outFilePath))
                {
                    LogSkipped(outFilePath);
                    return;
                }

                EnsureDirectoryExists(outFilePath);
                gltfExporter.Export(resource, outFilePath, cancellationToken);

                if (gltfExporter.FileLoader is TrackingFileLoader trackingFileLoader)
                {
                    lock (trackingFileLoader.LoadedFilePaths)
                    {
                        gltfExportedFiles.UnionWith(trackingFileLoader.LoadedFilePaths);
                        trackingFileLoader.LoadedFilePaths.Clear();
                    }
                }
            });
        }

        private ContentExporter CreateContentExporter(string outputRoot)
        {
            Log.Info(nameof(PackageExporter), $"Writing files to \"{outputRoot}\"");

            return new ContentExporter(outputRoot)
            {
                IncludeDependencies = includeDependencies,
                Progress = progress,
                ConfirmExistingFiles = ConfirmExistingFiles,
                FileWritten = path => progress?.SetProgress($"+ {ContentPaths.GetRelativePath(outputRoot, path)}"),
                FileSkipped = LogSkipped,
                Warning = message => Log.Warn(nameof(PackageExporter), message),
                FileFailed = ReportFailure,
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

        private void EnsureDirectoryExists(string outFilePath)
        {
            var directory = Path.GetDirectoryName(outFilePath);

            // Consecutive files usually share a folder, so avoid hitting the filesystem for each one
            if (directory == null || directory == lastCreatedDirectory)
            {
                return;
            }

            Directory.CreateDirectory(directory);
            lastCreatedDirectory = directory;
        }
    }
}

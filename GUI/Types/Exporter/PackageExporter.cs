using System.Collections.Frozen;
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
        private class FileTypeToExtract(PackageEntry sample)
        {
            /// <summary>Gets one file of the type, to tell what the type is converted to by reading it.</summary>
            public PackageEntry Sample { get; } = sample;

            public string? OutputFormat;
            public int Count = 1;
        }

        // Small files are written in a couple of chunks anyway, so reserving space up front only helps for large ones
        private const int PreallocationThreshold = 1024 * 1024;

        // When decompiling, parents go first so that anything they pull in (and export themselves) is skipped later
        private static readonly FrozenDictionary<string, int> ExtractOrder = new List<ResourceType>
        {
            ResourceType.Map,
            ResourceType.World,
            ResourceType.WorldNode,
            ResourceType.Model,
            ResourceType.Mesh,
            ResourceType.AnimationGroup,
            ResourceType.Animation,
            ResourceType.Sequence,
            ResourceType.Morph,

            ResourceType.Material,
            ResourceType.Texture,
        }.Select(static (type, index) => (Key: type.GetExtension() + GameFileLoader.CompiledFileSuffix, Index: index))
        .ToFrozenDictionary(static x => x.Key, static x => x.Index);

        private readonly bool decompile;
        private readonly ExportData exportData;
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
        private readonly List<PackageEntry> filesToExtract = [];
        private readonly HashSet<string> extractedFiles = [];
        private readonly List<string> extractedFolders = [];
        private string? path;
        private string? lastCreatedDirectory;
        private int processed;
        private int filesFailedToExport;
        private Exception? firstFailure;
        private string? firstFailedFile;
        private GenericProgressForm? progress;
        private GltfModelExporter? gltfExporter;

        private string OutputPath => path ?? throw new InvalidOperationException("Path must be set before extracting files");

        public PackageExporter(ExportData exportData, string? path, bool decompile)
        {
            this.exportData = exportData;
            this.path = path;
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
                fileTypesToExtract[file.TypeName] = new FileTypeToExtract(file); // Type to be filled in later
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

            if (decompile && ShowTypesDialog() != DialogResult.Continue)
            {
                return;
            }

            var selectedPath = AppFileDialogs.PickFolder("Choose which folder to extract files to", AppFileDialogs.RememberIn.SaveDirectory);

            if (selectedPath == null)
            {
                return;
            }

            path = selectedPath;

            // Nothing outside the export owns the files it reads, so there is nothing to wait for here
            RunInDialog(ExtractQueuedFilesAsync, null, out _);
        }

        /// <summary>
        /// Extracts a single already-read resource to <paramref name="outFilePath"/>, including any additional and sub files,
        /// behind a modal progress dialog.
        /// </summary>
        public Task ExecuteSingleFileExtractAsync(Resource resource, string inFilePath, string outFilePath, string initialText)
        {
            RunInDialog(cancellationToken => ExtractFileAsync(resource, inFilePath, outFilePath, true, exportData, cancellationToken), initialText, out var workCompletion);
            return workCompletion;
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
                gltfExporter = CreateGltfExporter(exportData.FileLoader);
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

        // The tracking loader tells which files the glTF exporter handled
        private GltfModelExporter CreateGltfExporter(IFileLoader fileLoader) => new(new TrackingFileLoader(fileLoader))
        {
            ProgressReporter = progress,
        };

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

            var context = exportData.VrfGuiContext.GetEntryDisplayPath(firstFailedFile);

            var report = new StringBuilder();
            ErrorReport.AppendExceptionWithVersion(report, firstFailure, context);

            ErrorReport.ShowDialog("Some files failed to export", text.ToString().TrimEnd(), report.ToString());
        }

        private DialogResult ShowTypesDialog()
        {
            using var typesDialog = new ExtractOutputTypesForm();
            typesDialog.ChangeTypeEvent += OnTypesDialogSelectedValueChanged;

            var hasVmap = fileTypesToExtract.Any(x => x.Key == "vmap_c");

            foreach (var type in fileTypesToExtract.OrderByDescending(x => x.Value.Count))
            {
                var resourceType = ResourceTypeExtensions.DetermineByFileExtension("." + type.Key);
                string firstType;

                if (resourceType != ResourceType.Unknown)
                {
                    firstType = FileExtract.GetExtension(resourceType);
                }
                else if (type.Key.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase))
                {
                    firstType = type.Key[..^2];
                }
                else
                {
                    firstType = GetConvertedType(type.Value.Sample) ?? type.Key;
                }

                var outputTypes = new List<string>()
                {
                    "* Do not export *",
                    firstType,
                };

                if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod" or "vnmclip" or "vnmskel")
                {
                    outputTypes.Add("gltf");
                    outputTypes.Add("glb");
                }
                else if (firstType == "vtex")
                {
                    outputTypes.Insert(1, "image");
                }
                else if (firstType == "vsnd")
                {
                    outputTypes.Insert(1, "sound");
                }

                // Select first suggested type, the 0th item is always "do not export"
                // For folders containing a map, default to "do not export", except for the map itself.
                var selectedIndex = hasVmap
                    ? firstType is "vmap" ? 1 : 0
                    : 1;

                type.Value.OutputFormat = selectedIndex == 0 ? null : outputTypes[selectedIndex];
                typesDialog.AddTypeToTable(type.Key, type.Value.Count, outputTypes, selectedIndex);
            }

            var result = typesDialog.ShowDialog();
            typesDialog.ChangeTypeEvent -= OnTypesDialogSelectedValueChanged;

            return result;
        }

        // The Source 2 type that a Source 1 or GoldSrc file is converted to when decompiling, found by reading the file
        private string? GetConvertedType(PackageEntry file)
        {
            var package = exportData.VrfGuiContext.CurrentPackage;

            if (package == null)
            {
                return null;
            }

            using var stream = package.GetMemoryMappedStreamIfPossible(file);
            var format = ConvertedExport.GetFormat(file.GetFullPath(), stream);

            return format.IsSupported ? FileExtract.GetExtension(format.ResourceType) : null;
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

            // TODO: Remember last selected value in settings?
            if (control.SelectedIndex == 0)
            {
                fileTypesToExtract[type].OutputFormat = null;
                return;
            }

            fileTypesToExtract[type].OutputFormat = control.SelectedItem as string;
        }

        private async Task ExtractQueuedFilesAsync(CancellationToken cancellationToken)
        {
            Debug.Assert(progress != null);

            Log.Info(nameof(PackageExporter), $"Folder export started to \"{path}\"");

            processed = 0;
            progress.SetBarMax(filesToExtract.Count);

            var outputPath = OutputPath;
            var currentPackage = exportData.VrfGuiContext.CurrentPackage;

            if (currentPackage == null)
            {
                ReportFailure(exportData.VrfGuiContext.FileName, new InvalidOperationException("The package is not available, cannot extract files"));
                return;
            }

            // Within each type, read the package in storage order to avoid seeking back and forth between entries
            var files = filesToExtract
                .OrderBy(file => decompile ? ExtractOrder.GetValueOrDefault(file.TypeName, ExtractOrder.Count) : 0)
                .ThenBy(static file => file.ArchiveIndex)
                .ThenBy(static file => file.Offset);

            foreach (var packageFile in files)
            {
                cancellationToken.ThrowIfCancellationRequested();

                progress.SetBarValue(++processed);

                string? outputFormat = null;

                if (decompile && fileTypesToExtract.TryGetValue(packageFile.TypeName, out var outputType))
                {
                    if (outputType.OutputFormat == null)
                    {
                        // Skip this file type
                        continue;
                    }

                    outputFormat = outputType.OutputFormat;
                }

                var fileFullName = packageFile.GetFullPath();

                if (extractedFiles.Contains(fileFullName) || IsInExtractedFolder(fileFullName))
                {
                    continue;
                }

                progress.SetProgress(fileFullName);

                var outFilePath = Path.Combine(outputPath, fileFullName);

                if (outputFormat != null)
                {
                    outFilePath = Path.ChangeExtension(outFilePath, outputFormat);
                }

                try
                {
                    EnsureDirectoryExists(outFilePath);

                    using var stream = currentPackage.GetMemoryMappedStreamIfPossible(packageFile);

                    var isCompiledResource = packageFile.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal);

                    if (decompile && !isCompiledResource && await TryExtractConvertedAsync(fileFullName, stream, outFilePath, cancellationToken).ConfigureAwait(false))
                    {
                        continue;
                    }

                    if (!decompile || !isCompiledResource)
                    {
                        // Extract as is
                        using var outStream = new FileStream(outFilePath, new FileStreamOptions
                        {
                            Mode = FileMode.Create,
                            Access = FileAccess.Write,
                            Options = FileOptions.Asynchronous,
                            PreallocationSize = packageFile.TotalLength >= PreallocationThreshold ? packageFile.TotalLength : 0,
                        });
                        await stream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);

                        continue;
                    }

                    using var resource = new Resource
                    {
                        FileName = fileFullName,
                    };

                    resource.Read(stream);

                    await ExtractFileAsync(resource, fileFullName, outFilePath, false, exportData, cancellationToken).ConfigureAwait(false);
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    ReportFailure(fileFullName, e);
                }
            }
        }

        private bool IsInExtractedFolder(string fileFullName)
        {
            foreach (var folder in extractedFolders)
            {
                if (fileFullName.StartsWith(folder, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        // Converts a Source 1 or GoldSrc file, which loads the files it refers to from its own context
        private async Task<bool> TryExtractConvertedAsync(string fileName, Stream stream, string outFilePath, CancellationToken cancellationToken)
        {
            var format = ConvertedExport.GetFormat(fileName, stream);

            if (!format.IsSupported)
            {
                return false;
            }

            using var converted = ConvertedExport.Convert(fileName, stream, format, exportData.VrfGuiContext);

            var convertedData = new ExportData
            {
                VrfGuiContext = converted.Context,
            };

            await ExtractFileAsync(converted.Resource, fileName, outFilePath, false, convertedData, cancellationToken).ConfigureAwait(false);

            return true;
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

        /// <param name="source">Serves the files that the resource refers to, which is the export data of the run for a file of the package.</param>
        private async Task ExtractFileAsync(Resource resource, string inFilePath, string outFilePath, bool flatSubfiles, ExportData source, CancellationToken cancellationToken)
        {
            var outExtension = Path.GetExtension(outFilePath);

            if (GltfModelExporter.CanExport(resource) && outExtension is ".glb" or ".gltf")
            {
                var gltf = source == exportData ? gltfExporter : CreateGltfExporter(source.FileLoader);

                if (gltf == null)
                {
                    ReportFailure(inFilePath, new InvalidOperationException("The glTF exporter is not available"));
                    return;
                }

                try
                {
                    gltf.Export(resource, outFilePath, cancellationToken);

                    if (gltf.FileLoader is TrackingFileLoader trackingFileLoader)
                    {
                        lock (trackingFileLoader.LoadedFilePaths)
                        {
                            extractedFiles.UnionWith(trackingFileLoader.LoadedFilePaths);
                            trackingFileLoader.LoadedFilePaths.Clear();
                        }
                    }
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception e)
                {
                    ReportFailure(inFilePath, e);
                }

                return;
            }

            ContentFile? contentFile = null;

            try
            {
                if (outExtension == ".sound" || outExtension == ".image" || outExtension.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    var extension = FileExtract.GetExtension(resource);

                    if (extension != null)
                    {
                        outFilePath = Path.ChangeExtension(outFilePath, extension);
                    }
                }
                else if (outExtension == ".vmap")
                {
                    flatSubfiles = false;
                }

                contentFile = FileExtract.Extract(resource, source.FileLoader, progress);

                if (contentFile.Data != null)
                {
                    await WriteFileAsync(outFilePath, contentFile.Data, cancellationToken).ConfigureAwait(false);
                }

                await ExtractAdditionalFilesAsync(contentFile, flatSubfiles, cancellationToken).ConfigureAwait(false);

                extractedFiles.Add(inFilePath);

                var inFileContentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(inFilePath) ?? string.Empty;

                await ExtractSubfilesAsync(inFileContentRelativeFolder, contentFile, cancellationToken).ConfigureAwait(false);
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception e)
            {
                ReportFailure(inFilePath, e);
            }
            finally
            {
                contentFile?.Dispose();
            }
        }

        private async Task ExtractAdditionalFilesAsync(ContentFile contentFile, bool flatSubfiles, CancellationToken cancellationToken)
        {
            foreach (var additionalFile in contentFile.AdditionalFiles)
            {
                if (additionalFile.FileName.EndsWith('/'))
                {
                    extractedFolders.Add(additionalFile.FileName);
                    continue;
                }

                extractedFiles.Add(additionalFile.FileName + GameFileLoader.CompiledFileSuffix);
                var fileNameOut = additionalFile.FileName;
                var flattenThis = flatSubfiles && !additionalFile.KeepFullPath;

                if (additionalFile.Data != null)
                {
                    if (flattenThis)
                    {
                        fileNameOut = Path.GetFileName(fileNameOut);
                    }

                    await WriteAssetFileAsync(fileNameOut, additionalFile.Data, cancellationToken).ConfigureAwait(false);
                }

                var contentRelativeFolder = flattenThis ? string.Empty : Path.GetDirectoryName(fileNameOut) ?? string.Empty;

                await ExtractSubfilesAsync(contentRelativeFolder, additionalFile, cancellationToken).ConfigureAwait(false);
                await ExtractAdditionalFilesAsync(additionalFile, flatSubfiles, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ExtractSubfilesAsync(string contentRelativeFolder, ContentFile contentFile, CancellationToken cancellationToken)
        {
            foreach (var contentSubFile in contentFile.SubFiles)
            {
                cancellationToken.ThrowIfCancellationRequested();
                contentSubFile.FileName = Path.Combine(contentRelativeFolder, contentSubFile.FileName).Replace(Path.DirectorySeparatorChar, '/');

                if (extractedFiles.Contains(contentSubFile.FileName))
                {
                    continue;
                }

                if (contentSubFile.Extract == null)
                {
                    ReportFailure(contentSubFile.FileName, new InvalidOperationException("The subfile has no extract function"));
                    continue;
                }

                byte[] subFileData;
                try
                {
                    subFileData = contentSubFile.Extract.Invoke();
                }
                catch (Exception e) when (e is not OperationCanceledException)
                {
                    ReportFailure(contentSubFile.FileName, e);
                    continue;
                }

                if (subFileData.Length > 0)
                {
                    extractedFiles.Add(contentSubFile.FileName);
                    await WriteAssetFileAsync(contentSubFile.FileName, subFileData, cancellationToken).ConfigureAwait(false);
                }
            }
        }

        private Task WriteAssetFileAsync(string assetName, byte[] data, CancellationToken cancellationToken)
        {
            progress?.SetProgress($"+ {assetName}");
            return WriteFileAsync(CombineAssetFolder(OutputPath, assetName), data, cancellationToken);
        }

        private Task WriteFileAsync(string outFilePath, byte[] data, CancellationToken cancellationToken)
        {
            EnsureDirectoryExists(outFilePath);
            return File.WriteAllBytesAsync(outFilePath, data, cancellationToken);
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

        private static string CombineAssetFolder(string userFolder, string assetName)
        {
            var normalized = userFolder.Replace(Path.DirectorySeparatorChar, '/');

            // Chop the longest run of leading asset folders that the user folder already ends with
            for (var slash = assetName.LastIndexOf('/'); slash > 0; slash = assetName.LastIndexOf('/', slash - 1))
            {
                if (normalized.Length >= slash
                    && normalized.AsSpan(normalized.Length - slash).SequenceEqual(assetName.AsSpan(0, slash))
                    && (normalized.Length == slash || normalized[normalized.Length - slash - 1] == '/'))
                {
                    return Path.Combine(userFolder, assetName[(slash + 1)..]);
                }
            }

            return Path.Combine(userFolder, assetName);
        }
    }
}

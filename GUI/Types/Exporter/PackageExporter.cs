using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;
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

        private static readonly List<ResourceType> ExtractOrder =
        [
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
        ];

        private readonly bool decompile;
        private readonly ExportData exportData;
        private readonly Dictionary<string, Queue<PackageEntry>> filesToExtractSorted = [];
        private readonly List<Queue<PackageEntry>> sortedQueues = [];
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
        private readonly Queue<PackageEntry> filesToExtract = new();
        private readonly HashSet<string> extractedFiles = [];
        private string? path;
        private string? lastCreatedDirectory;
        private int queuedCount;
        private int processed;
        private int filesFailedToExport;
        private GenericProgressForm? progress;
        private GltfModelExporter? gltfExporter;

        private string OutputPath => path ?? throw new InvalidOperationException("Path must be set before extracting files");

        public PackageExporter(ExportData exportData, string? path, bool decompile)
        {
            this.exportData = exportData;
            this.path = path;
            this.decompile = decompile;

            foreach (var resourceType in ExtractOrder)
            {
                var queue = new Queue<PackageEntry>();
                filesToExtractSorted.Add(resourceType.GetExtension() + GameFileLoader.CompiledFileSuffix, queue);
                sortedQueues.Add(queue);
            }
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

            queuedCount++;

            if (decompile && filesToExtractSorted.TryGetValue(file.TypeName, out var specializedQueue))
            {
                specializedQueue.Enqueue(file);
                return;
            }

            filesToExtract.Enqueue(file);
        }

        /// <summary>
        /// Asks for output types and a destination folder, then extracts all queued files behind a modal progress dialog.
        /// </summary>
        public void ExecuteMultipleFileExtract()
        {
            if (queuedCount == 0)
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

            RunInDialog(ExtractQueuedFilesAsync, null);
        }

        /// <summary>
        /// Extracts a single already-read resource to <paramref name="outFilePath"/>, including any additional and sub files,
        /// behind a modal progress dialog.
        /// </summary>
        public void ExecuteSingleFileExtract(Resource resource, string inFilePath, string outFilePath, string initialText)
        {
            RunInDialog(cancellationToken => ExtractFileAsync(resource, inFilePath, outFilePath, true, cancellationToken), initialText);
        }

        private void RunInDialog(Func<CancellationToken, Task> work, string? initialText)
        {
            using var dialog = new GenericProgressForm();
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

#pragma warning disable CA2025 // The dialog is modal, the task has completed by the time ShowDialog returns
            dialog.OnProcess = work;
#pragma warning restore CA2025
            dialog.ShowDialog();

            if (filesFailedToExport > 0)
            {
                _ = AppMessageDialogs.ShowMessageAsync(
                    $"{filesFailedToExport} file{(filesFailedToExport == 1 ? "" : "s")} failed to extract, see the console for details.",
                    "Export finished with errors",
                    MessageIcon.Warning);
            }
        }

        private DialogResult ShowTypesDialog()
        {
            using var typesDialog = new ExtractOutputTypesForm();
            typesDialog.ChangeTypeEvent += OnTypesDialogSelectedValueChanged;

            var hasVmap = fileTypesToExtract.Any(x => x.Key == "vmap_c");

            foreach (var type in fileTypesToExtract.OrderByDescending(x => x.Value.Count))
            {
                var resourceType = ResourceTypeExtensions.DetermineByFileExtension("." + type.Key);
                var firstType = resourceType != ResourceType.Unknown
                    ? FileExtract.GetExtension(resourceType)
                    : type.Key.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase) ? type.Key[..^2] : type.Key;

                var outputTypes = new List<string>()
                {
                    "* Do not export *",
                    firstType,
                };

                if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod" or "vnmclip")
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

            var stopwatch = Stopwatch.StartNew();
            Log.Info(nameof(PackageExporter), $"Folder export started to \"{path}\"");

            processed = 0;
            progress.SetBarMax(queuedCount);

            if (decompile)
            {
                foreach (var files in sortedQueues)
                {
                    if (files.Count > 0)
                    {
                        await ExtractFilesAsync(files, cancellationToken).ConfigureAwait(false);
                    }
                }
            }

            await ExtractFilesAsync(filesToExtract, cancellationToken).ConfigureAwait(false);

            Log.Info(nameof(PackageExporter), $"Export completed in {stopwatch.Elapsed}, {filesFailedToExport} files failed.");
        }

        private async Task ExtractFilesAsync(Queue<PackageEntry> files, CancellationToken cancellationToken)
        {
            Debug.Assert(progress != null);

            var outputPath = OutputPath;
            var currentPackage = exportData.VrfGuiContext.CurrentPackage;

            if (currentPackage == null)
            {
                Log.Error(nameof(PackageExporter), "CurrentPackage is null, cannot extract files");
                return;
            }

            while (files.Count > 0)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var packageFile = files.Dequeue();
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

                if (extractedFiles.Contains(fileFullName))
                {
                    continue;
                }

                progress.SetProgress(fileFullName);

                var outFilePath = Path.Combine(outputPath, fileFullName);

                if (outputFormat != null)
                {
                    outFilePath = Path.ChangeExtension(outFilePath, outputFormat);
                }

                EnsureDirectoryExists(outFilePath);

                var stream = GameFileLoader.GetPackageEntryStream(currentPackage, packageFile);

                if (!decompile || !packageFile.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    // Extract as is
                    using var outStream = File.Create(outFilePath);
                    await stream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);

                    continue;
                }

                using var resource = new Resource
                {
                    FileName = fileFullName,
                };

                try
                {
                    resource.Read(stream);
                }
                catch (Exception e)
                {
                    filesFailedToExport++;
                    Log.Error(nameof(PackageExporter), $"Failed to extract '{fileFullName}': {e}");
                    continue;
                }

                await ExtractFileAsync(resource, fileFullName, outFilePath, false, cancellationToken).ConfigureAwait(false);
            }
        }

        private async Task ExtractFileAsync(Resource resource, string inFilePath, string outFilePath, bool flatSubfiles, CancellationToken cancellationToken)
        {
            var outputPath = OutputPath;
            var outExtension = Path.GetExtension(outFilePath);

            if (GltfModelExporter.CanExport(resource) && outExtension is ".glb" or ".gltf")
            {
                if (gltfExporter == null)
                {
                    Log.Error(nameof(PackageExporter), "gltfExporter is null, cannot export to glTF format");
                    return;
                }

                try
                {
                    gltfExporter.Export(resource, outFilePath, cancellationToken);

                    if (gltfExporter.FileLoader is TrackingFileLoader trackingFileLoader)
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
                    filesFailedToExport++;
                    Log.Error(nameof(PackageExporter), $"Failed to extract '{resource.FileName}': {e}");
                }

                return;
            }

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

            ContentFile? contentFile = null;

            try
            {
                contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext, progress);

                if (contentFile.Data != null)
                {
                    await WriteFileAsync(outFilePath, contentFile.Data, cancellationToken).ConfigureAwait(false);
                }

                foreach (var additionalFile in contentFile.AdditionalFiles)
                {
                    extractedFiles.Add(additionalFile.FileName + GameFileLoader.CompiledFileSuffix);
                    var fileNameOut = additionalFile.FileName;
                    var flattenThis = flatSubfiles && !additionalFile.KeepFullPath;

                    if (additionalFile.Data != null)
                    {
                        if (flattenThis)
                        {
                            fileNameOut = Path.GetFileName(fileNameOut);
                        }

                        await WriteFileAsync(CombineAssetFolder(outputPath, fileNameOut), additionalFile.Data, cancellationToken).ConfigureAwait(false);
                    }

                    var contentRelativeFolder = flattenThis ? string.Empty : Path.GetDirectoryName(fileNameOut) ?? string.Empty;

                    await ExtractSubfilesAsync(contentRelativeFolder, additionalFile, cancellationToken).ConfigureAwait(false);
                }

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
                filesFailedToExport++;
                Log.Error(nameof(PackageExporter), $"Failed to extract '{inFilePath}': {e}");
            }
            finally
            {
                contentFile?.Dispose();
            }
        }

        private async Task ExtractSubfilesAsync(string contentRelativeFolder, ContentFile contentFile, CancellationToken cancellationToken)
        {
            var outputPath = OutputPath;

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
                    Log.Error(nameof(PackageExporter), $"Extract function is null for subfile '{contentSubFile.FileName}'");
                    continue;
                }

                byte[] subFileData;
                try
                {
                    subFileData = contentSubFile.Extract.Invoke();
                }
                catch (Exception e)
                {
                    filesFailedToExport++;
                    Log.Error(nameof(PackageExporter), $"Failed to extract subfile '{contentSubFile.FileName}': {e}");
                    continue;
                }

                if (subFileData.Length > 0)
                {
                    extractedFiles.Add(contentSubFile.FileName);
                    await WriteFileAsync(CombineAssetFolder(outputPath, contentSubFile.FileName), subFileData, cancellationToken).ConfigureAwait(false);
                }
            }
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
            var assetFolders = assetName.Split('/')[..^1];
            var userFolders = userFolder.Split(Path.DirectorySeparatorChar);

            var leftChop = 0;

            foreach (var i in Enumerable.Range(0, assetFolders.Length))
            {
                if (Enumerable.SequenceEqual(
                    assetFolders.Reverse().Skip(i),
                    userFolders.Reverse().Take(assetFolders.Length - i)
                ))
                {
                    leftChop = assetFolders.Reverse().Skip(i).Sum(static x => x.Length + 1);
                }
            }

            return Path.Combine(userFolder, assetName[leftChop..]);
        }
    }
}

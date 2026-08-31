using System.Collections.Frozen;
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
        private string? path;
        private string? lastCreatedDirectory;
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
            RunInDialog(cancellationToken => ExtractFileAsync(resource, inFilePath, outFilePath, true, cancellationToken), initialText, out var workCompletion);
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
                    completedText += $", {filesFailedToExport} file{(filesFailedToExport == 1 ? "" : "s")} failed";
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

                if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod" or "vnmclip" or "vanim" or "vskel" or "vnmskel")
                {
                    outputTypes.Add("gltf");
                    outputTypes.Add("glb");
                    outputTypes.Add("smd");
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

            Log.Info(nameof(PackageExporter), $"Folder export started to \"{path}\"");

            processed = 0;
            progress.SetBarMax(filesToExtract.Count);

            var outputPath = OutputPath;
            var currentPackage = exportData.VrfGuiContext.CurrentPackage;

            if (currentPackage == null)
            {
                Log.Error(nameof(PackageExporter), "CurrentPackage is null, cannot extract files");
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
                    using var rawStream = stream;
                    using var outStream = new FileStream(outFilePath, new FileStreamOptions
                    {
                        Mode = FileMode.Create,
                        Access = FileAccess.Write,
                        Options = FileOptions.Asynchronous,
                        PreallocationSize = packageFile.TotalLength >= PreallocationThreshold ? packageFile.TotalLength : 0,
                    });
                    await rawStream.CopyToAsync(outStream, cancellationToken).ConfigureAwait(false);

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

                if (outExtension == ".smd")
                {
                    if (resource.ResourceType == ResourceType.NmClip)
                    {
                        var clipExtract = new NmClipExtract(resource, exportData.VrfGuiContext);
                        var smdBytes = clipExtract.ToSmdData().ToBytes();
                        await WriteFileAsync(outFilePath, smdBytes, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    if (resource.ResourceType == ResourceType.NmSkeleton)
                    {
                        var skelExtract = new NmSkeletonExtract(resource);
                        var smdBytes = skelExtract.ToSmdData().ToBytes();
                        await WriteFileAsync(outFilePath, smdBytes, cancellationToken).ConfigureAwait(false);
                        return;
                    }

                    var smdSubFile = contentFile.SubFiles.FirstOrDefault(f => f.FileName.EndsWith(".smd", StringComparison.OrdinalIgnoreCase));
                    if (smdSubFile != null)
                    {
                        var smdBytes = smdSubFile.Extract?.Invoke();
                        if (smdBytes != null)
                        {
                            await WriteFileAsync(outFilePath, smdBytes, cancellationToken).ConfigureAwait(false);
                            return;
                        }
                    }
                }

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

                        await WriteAssetFileAsync(fileNameOut, additionalFile.Data, cancellationToken).ConfigureAwait(false);
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

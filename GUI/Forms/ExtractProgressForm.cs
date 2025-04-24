using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
using GUI.Types.PackageViewer;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

#nullable disable

namespace GUI.Forms
{
    partial class ExtractProgressForm : Form
    {
        private class ExtractProgress(Action<string> SetProgress) : IProgress<string>
        {
            public void Report(string value) => SetProgress(value);
        }

        private class FileTypeToExtract
        {
            public string OutputFormat;
            public int Count = 1;
        }

        private readonly bool decompile;
        private string path;
        private readonly ExportData exportData;
        private readonly Dictionary<string, Queue<PackageEntry>> filesToExtractSorted = [];
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = [];
        private readonly Queue<PackageEntry> filesToExtract = new();
        private readonly HashSet<string> extractedFiles = [];
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly GltfModelExporter gltfExporter;
        private readonly IProgress<string> progressReporter;
        private Stopwatch exportStopwatch;
        private int filesFailedToExport;

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

        public Action<ExtractProgressForm, CancellationToken> ShownCallback { get; init; }

        public ExtractProgressForm(ExportData exportData, string path, bool decompile)
        {
            InitializeComponent();

            foreach (var resourceType in ExtractOrder)
            {
                var extension = resourceType.GetExtension();
                filesToExtractSorted.Add(extension + GameFileLoader.CompiledFileSuffix, new());
            }

            this.path = path;
            this.decompile = decompile;
            this.exportData = exportData;
            progressReporter = new ExtractProgress(SetProgress);

            if (decompile)
            {
                // We need to know what files were handled by the glTF exporter
                var trackingFileLoader = new TrackingFileLoader(exportData.VrfGuiContext.FileLoader);

                gltfExporter = new GltfModelExporter(trackingFileLoader)
                {
                    ProgressReporter = progressReporter,
                };
            }
        }

        public void Execute()
        {
            if (filesToExtract.Count == 0 && filesToExtractSorted.Sum(x => x.Value.Count) == 0)
            {
                MessageBox.Show("There are no files to extract", "Failed to extract");
                return;
            }

            if (decompile && ShowTypesDialog() != DialogResult.Continue)
            {
                return;
            }

            using var dialog = new FolderBrowserDialog
            {
                Description = "Choose which folder to extract files to",
                UseDescriptionForTitle = true,
                SelectedPath = Settings.Config.SaveDirectory,
                AddToRecent = true,
            };

            if (dialog.ShowDialog() != DialogResult.OK)
            {
                return;
            }

            path = dialog.SelectedPath;
            Settings.Config.SaveDirectory = dialog.SelectedPath;

            ShowDialog();
        }

        private DialogResult ShowTypesDialog()
        {
            using var typesDialog = new ExtractOutputTypesForm();
            typesDialog.ChangeTypeEvent += OnTypesDialogSelectedValueChanged;

            foreach (var type in fileTypesToExtract.OrderByDescending(x => x.Value.Count))
            {
                /// See <see cref="ResourceTypeExtensions.GetExtension(ResourceType)"/>
                var firstType = type.Key switch
                {
                    "vjs_c" => "js",
                    "vts_c" => "js",
                    "vxml_c" => "xml",
                    "vcss_c" => "css",
                    "vsvg_c" => "svg",
                    _ when type.Key.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.OrdinalIgnoreCase) => type.Key[..^2],
                    _ => type.Key,
                };

                var outputTypes = new List<string>()
                {
                    "* Do not export *",
                    firstType,
                };

                if (firstType is "vmdl" or "vmesh" or "vmap" or "vwrld" or "vwnod")
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

                type.Value.OutputFormat = outputTypes[1];

                // Select first suggested type, the 0th item is always "do not export"
                typesDialog.AddTypeToTable(type.Key, type.Value.Count, outputTypes, 1);
            }

            var result = typesDialog.ShowDialog();
            typesDialog.ChangeTypeEvent -= OnTypesDialogSelectedValueChanged;

            return result;
        }

        private void OnTypesDialogSelectedValueChanged(object sender, EventArgs e)
        {
            var control = (ComboBox)sender;
            var type = (string)control.Tag!;

            // TODO: Remember last selected value in settings?
            if (control.SelectedIndex == 0)
            {
                fileTypesToExtract[type].OutputFormat = null;
                return;
            }

            fileTypesToExtract[type].OutputFormat = (string)control.SelectedItem;
        }

        protected override void OnShown(EventArgs e)
        {
            exportStopwatch = Stopwatch.StartNew();

            if (ShownCallback != null)
            {
                extractProgressBar.Style = ProgressBarStyle.Marquee;
                ShownCallback(this, cancellationTokenSource.Token);
                return;
            }

            Task.Run(async () =>
            {
                SetProgress($"Folder export started to \"{path}\"");

                if (decompile)
                {
                    foreach (var resourceType in ExtractOrder)
                    {
                        var extension = resourceType.GetExtension();
                        var files = filesToExtractSorted[extension + GameFileLoader.CompiledFileSuffix];

                        if (files.Count > 0)
                        {
                            SetProgress($"Extracting {resourceType}s…");
                            await ExtractFilesAsync(files).ConfigureAwait(false);
                        }
                    }

                    if (filesToExtract.Count > 0)
                    {
                        SetProgress("Extracting files…");
                    }
                }

                await ExtractFilesAsync(filesToExtract).ConfigureAwait(false);
            }, cancellationTokenSource.Token).ContinueWith(ExportContinueWith, CancellationToken.None);
        }

        public void ExportContinueWith(Task t)
        {
            exportStopwatch.Stop();

            if (t.IsFaulted)
            {
                var ex = t.Exception.ToString();
                Log.Error(nameof(ExtractProgressForm), ex);
                SetProgress(ex);

                cancellationTokenSource.Cancel();
            }

            Invoke(() =>
            {
                if (filesFailedToExport > 0)
                {
                    var nl = Environment.NewLine;
                    SetProgress($"WARNING:{nl}{nl}{filesFailedToExport} files failed to extract, check console for more information.{nl}");
                }

                Text = t.IsFaulted ? "Source 2 Viewer - Export failed, check console for details" : "Source 2 Viewer - Export completed";
                cancelButton.Text = "Close";
                extractProgressBar.Value = 100;
                extractProgressBar.Style = ProgressBarStyle.Blocks;
                extractProgressBar.Update();
            });

            SetProgress($"Export completed in {exportStopwatch.Elapsed}.");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        public void QueueFiles(IBetterBaseItem root)
        {
            if (root.IsFolder)
            {
                QueueFiles(root.PkgNode);
            }
            else
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

            if (decompile && filesToExtractSorted.TryGetValue(file.TypeName, out var specializedQueue))
            {
                specializedQueue.Enqueue(file);
                return;
            }

            filesToExtract.Enqueue(file);
        }

        private async Task ExtractFilesAsync(Queue<PackageEntry> filesToExtract)
        {
            var initialCount = filesToExtract.Count;
            while (filesToExtract.Count > 0)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var packageFile = filesToExtract.Dequeue();
                var fileFullName = packageFile.GetFullPath();

                if (extractedFiles.Contains(fileFullName))
                {
                    continue;
                }

                await InvokeAsync(() =>
                {
                    extractProgressBar.Value = 100 - (int)(filesToExtract.Count / (float)initialCount * 100.0f);
                }).ConfigureAwait(false);

                var stream = AdvancedGuiFileLoader.GetPackageEntryStream(exportData.VrfGuiContext.CurrentPackage, packageFile);
                var outFilePath = Path.Combine(path, fileFullName);
                var outFolder = Path.GetDirectoryName(outFilePath);

                if (decompile && fileTypesToExtract.TryGetValue(packageFile.TypeName, out var outputType))
                {
                    if (outputType.OutputFormat == null)
                    {
                        // Skip this file type
                        continue;
                    }

                    outFilePath = Path.ChangeExtension(outFilePath, outputType.OutputFormat);
                }

                SetProgress($"Extracting {fileFullName}");

                Directory.CreateDirectory(outFolder);

                if (!decompile || !packageFile.TypeName.EndsWith(GameFileLoader.CompiledFileSuffix, StringComparison.Ordinal))
                {
                    // Extract as is
                    var outStream = File.OpenWrite(outFilePath);
                    await stream.CopyToAsync(outStream).ConfigureAwait(false);
                    outStream.Close();

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

                    Log.Error(nameof(ExtractProgressForm), $"Failed to extract '{fileFullName}': {e}");
                    SetProgress($"Failed to extract '{fileFullName}': {e.Message}");

                    continue;
                }

                await ExtractFile(resource, fileFullName, outFilePath).ConfigureAwait(false);
            }
        }

        public async Task ExtractFile(Resource resource, string inFilePath, string outFilePath, bool flatSubfiles = false)
        {
            var outExtension = Path.GetExtension(outFilePath);

            if (GltfModelExporter.CanExport(resource) && outExtension is ".glb" or ".gltf")
            {
                try
                {
                    gltfExporter.Export(resource, outFilePath, cancellationTokenSource.Token);

                    if (gltfExporter.FileLoader is TrackingFileLoader trackingFileLoader)
                    {
                        extractedFiles.UnionWith(trackingFileLoader.LoadedFilePaths);
                    }
                }
                catch (Exception e)
                {
                    filesFailedToExport++;

                    Log.Error(nameof(ExtractProgressForm), $"Failed to extract '{resource.FileName}': {e}");
                    SetProgress($"Failed to extract '{resource.FileName}': {e.Message}");
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

            ContentFile contentFile = null;

            try
            {
                contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext.FileLoader, progressReporter);

                if (contentFile.Data != null)
                {
                    SetProgress($"+ {outFilePath.Remove(0, path.Length + 1)}");
                    await File.WriteAllBytesAsync(outFilePath, contentFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                }

                string contentRelativeFolder;
                foreach (var additionalFile in contentFile.AdditionalFiles)
                {
                    extractedFiles.Add(additionalFile.FileName + GameFileLoader.CompiledFileSuffix);
                    var fileNameOut = additionalFile.FileName;

                    if (additionalFile.Data != null)
                    {
                        if (flatSubfiles)
                        {
                            fileNameOut = Path.GetFileName(fileNameOut);
                        }

                        var outPath = CombineAssetFolder(path, fileNameOut);
                        Directory.CreateDirectory(Path.GetDirectoryName(outPath.Full));
                        SetProgress($" + {outPath.Partial}");
                        await File.WriteAllBytesAsync(outPath.Full, additionalFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                    }

                    contentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(fileNameOut);

                    await ExtractSubfiles(contentRelativeFolder, additionalFile).ConfigureAwait(false);
                }

                extractedFiles.Add(inFilePath);

                contentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(inFilePath);

                await ExtractSubfiles(contentRelativeFolder, contentFile).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                filesFailedToExport++;

                Log.Error(nameof(ExtractProgressForm), $"Failed to extract '{inFilePath}': {e}");
                SetProgress($"Failed to extract '{inFilePath}': {e.Message}");
            }
            finally
            {
                contentFile?.Dispose();
            }
        }

        private async Task ExtractSubfiles(string contentRelativeFolder, ContentFile contentFile)
        {
            foreach (var contentSubFile in contentFile.SubFiles)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();
                contentSubFile.FileName = Path.Combine(contentRelativeFolder, contentSubFile.FileName).Replace(Path.DirectorySeparatorChar, '/');
                var outPath = CombineAssetFolder(path, contentSubFile.FileName);

                if (extractedFiles.Contains(contentSubFile.FileName))
                {
                    SetProgress($"  - {outPath.Partial}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(outPath.Full));

                byte[] subFileData;
                try
                {
                    subFileData = contentSubFile.Extract.Invoke();
                }
                catch (Exception e)
                {
                    filesFailedToExport++;

                    Log.Error(nameof(ExtractProgressForm), $"Failed to extract subfile '{contentSubFile.FileName}': {e}");
                    SetProgress($"Failed to extract subfile '{contentSubFile.FileName}': {e.Message}");
                    continue;
                }

                if (subFileData.Length > 0)
                {
                    SetProgress($"  + {outPath.Partial}");
                    extractedFiles.Add(contentSubFile.FileName);
                    await File.WriteAllBytesAsync(outPath.Full, subFileData, cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
        }

        private static (string Full, string Partial) CombineAssetFolder(string userFolder, string assetName)
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
                    leftChop = assetFolders.Reverse().Skip(i).Select(x => x.Length + 1).Sum();
                }
            }

            return (Path.Combine(userFolder, assetName[leftChop..]), assetName[leftChop..]);
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }

        public void SetProgress(string text)
        {
            if (Disposing || IsDisposed || cancellationTokenSource.IsCancellationRequested)
            {
                return;
            }

            var str = $"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}";

            if (progressLog.InvokeRequired)
            {
                progressLog.Invoke(progressLog.AppendText, str);
            }
            else
            {
                progressLog.AppendText(str);
            }
        }
    }
}

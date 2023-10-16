using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Controls;
using GUI.Types.Exporter;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat;
using ValveResourceFormat.IO;

namespace GUI.Forms
{
    partial class ExtractProgressForm : Form
    {
        private class FileTypeToExtract
        {
            public string OutputFormat;
            public int Count = 1;
        }

        private readonly bool decompile;
        private readonly string path;
        private readonly ExportData exportData;
        private readonly Dictionary<string, Queue<PackageEntry>> filesToExtractSorted = new();
        private readonly Dictionary<string, FileTypeToExtract> fileTypesToExtract = new();
        private readonly Queue<PackageEntry> filesToExtract = new();
        private readonly HashSet<string> extractedFiles = new();
        private CancellationTokenSource cancellationTokenSource = new();
        private readonly GltfModelExporter gltfExporter;
        private Stopwatch exportStopwatch;

        private static readonly List<ResourceType> ExtractOrder = new()
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
        };

        public Action<ExtractProgressForm, CancellationToken> ShownCallback { get; init; }

        public ExtractProgressForm(ExportData exportData, string path, bool decompile)
        {
            InitializeComponent();

            foreach (var resourceType in ExtractOrder)
            {
                var extension = FileExtract.GetExtension(resourceType);
                filesToExtractSorted.Add(extension + "_c", new());
            }

            this.path = path;
            this.decompile = decompile;
            this.exportData = exportData;

            if (decompile)
            {
                // We need to know what files were handled by the glTF exporter
                var trackingFileLoader = new TrackingFileLoader(exportData.VrfGuiContext.FileLoader);

                gltfExporter = new GltfModelExporter(trackingFileLoader)
                {
                    ProgressReporter = new Progress<string>(SetProgress),
                };
            }
        }

        public void Execute()
        {
            if (fileTypesToExtract.Count == 0)
            {
                ShowDialog();
                return;
            }

            void SelectedValueChanged(object sender, EventArgs e)
            {
                var control = (ComboBox)sender;
                var type = (string)control.Tag;

                if (control.SelectedIndex == 0)
                {
                    fileTypesToExtract[type].OutputFormat = null;
                    return;
                }

                fileTypesToExtract[type].OutputFormat = (string)control.SelectedItem;
            }

            using var typesDialog = new ExtractOutputTypesForm();
            typesDialog.ChangeTypeEvent += SelectedValueChanged;

            foreach (var type in fileTypesToExtract.OrderByDescending(x => x.Value.Count))
            {
                var firstType = type.Key.EndsWith("_c", StringComparison.OrdinalIgnoreCase)
                    ? type.Key[..^2].ToLowerInvariant()
                    : type.Key.ToLowerInvariant();

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

                /// TODO: Sounds and images, see <see cref="FileExtract.GetExtension"/>

                typesDialog.AddTypeToTable(type.Key, type.Value.Count, outputTypes);
            }

            var result = typesDialog.ShowDialog();
            typesDialog.ChangeTypeEvent -= SelectedValueChanged;

            if (result == DialogResult.Continue)
            {
                ShowDialog();
            }
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
                        var extension = FileExtract.GetExtension(resourceType);
                        var files = filesToExtractSorted[extension + "_c"];

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

        public void QueueFiles(BetterTreeNode root)
        {
            if (!root.IsFolder)
            {
                var file = root.PackageEntry;

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
                return;
            }

            foreach (BetterTreeNode node in root.Nodes)
            {
                QueueFiles(node);
            }
        }

        private async Task ExtractFilesAsync(Queue<PackageEntry> filesToExtract)
        {
            var initialCount = filesToExtract.Count;
            while (filesToExtract.Count > 0)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var packageFile = filesToExtract.Dequeue();

                if (extractedFiles.Contains(packageFile.GetFullPath()))
                {
                    continue;
                }

                Invoke(() =>
                {
                    extractProgressBar.Value = 100 - (int)(filesToExtract.Count / (float)initialCount * 100.0f);
                });

                SetProgress($"Extracting {packageFile.GetFullPath()}");

                var stream = AdvancedGuiFileLoader.GetPackageEntryStream(exportData.VrfGuiContext.CurrentPackage, packageFile);
                var outFilePath = Path.Combine(path, packageFile.GetFullPath());
                var outFolder = Path.GetDirectoryName(outFilePath);

                var type = packageFile.TypeName[..^2]; // Remove "_c"

                if (fileTypesToExtract.TryGetValue(type, out var outputType))
                {
                    if (outputType.OutputFormat == null)
                    {
                        // Skip this file type
                        continue;
                    }

                    outFilePath = Path.ChangeExtension(outFilePath, outputType.OutputFormat);
                }

                Directory.CreateDirectory(outFolder);

                if (!decompile || !outFilePath.EndsWith("_c", StringComparison.Ordinal))
                {
                    // Extract as is
                    var outStream = File.OpenWrite(outFilePath);
                    await stream.CopyToAsync(outStream).ConfigureAwait(false);
                    outStream.Close();

                    continue;
                }

                using var resource = new Resource
                {
                    FileName = packageFile.GetFullPath(),
                };
                resource.Read(stream);

                await ExtractFile(resource, packageFile.GetFullPath(), outFilePath).ConfigureAwait(false);
            }
        }

        public async Task ExtractFile(Resource resource, string inFilePath, string outFilePath, bool flatSubfiles = false)
        {
            if (GltfModelExporter.CanExport(resource) && Path.GetExtension(outFilePath) is ".glb" or ".gltf")
            {
                gltfExporter.Export(resource, outFilePath, cancellationTokenSource.Token);
                if (gltfExporter.FileLoader is TrackingFileLoader trackingFileLoader)
                {
                    extractedFiles.UnionWith(trackingFileLoader.LoadedFilePaths);
                }

                return;
            }

            // TODO: Use provided extension
            var extension = FileExtract.GetExtension(resource);

            if (extension == null)
            {
                outFilePath = outFilePath[..^2]; // remove "_c"
            }
            else
            {
                outFilePath = Path.ChangeExtension(outFilePath, extension);
            }

            ContentFile contentFile = null;
            if (outFilePath.EndsWith(".vmap", StringComparison.Ordinal))
            {
                flatSubfiles = false;
            }

            try
            {
                contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext.FileLoader);

                if (contentFile.Data != null)
                {
                    SetProgress($"+ {outFilePath.Remove(0, path.Length + 1)}");
                    await File.WriteAllBytesAsync(outFilePath, contentFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                }

                string contentRelativeFolder;
                foreach (var additionalFile in contentFile.AdditionalFiles)
                {
                    extractedFiles.Add(additionalFile.FileName + "_c");
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

            Invoke(() =>
            {
                progressLog.BeginUpdate();
                progressLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
                progressLog.GoEnd();
                progressLog.EndUpdate();
            });
        }
    }
}

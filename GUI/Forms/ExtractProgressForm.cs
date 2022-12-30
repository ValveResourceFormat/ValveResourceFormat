using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
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
    public partial class ExtractProgressForm : Form
    {
        private bool decompile;
        private readonly string path;
        private readonly ExportData exportData;
        private readonly Dictionary<string, Queue<PackageEntry>> filesToExtractSorted;
        private readonly Queue<PackageEntry> filesToExtract;
        private readonly HashSet<string> extractedFiles;
        private readonly GltfModelExporter gltfExporter;
        private CancellationTokenSource cancellationTokenSource;

        private static readonly List<ResourceType> ExtractOrder = new()
        {
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

            cancellationTokenSource = new CancellationTokenSource();

            filesToExtractSorted = new();
            foreach (var resourceType in ExtractOrder)
            {
                var extension = FileExtract.GetExtension(resourceType);
                filesToExtractSorted.Add(extension + "_c", new Queue<PackageEntry>());
            }
            filesToExtract = new Queue<PackageEntry>();
            extractedFiles = new HashSet<string>();

            this.path = path;
            this.decompile = decompile;
            this.exportData = exportData;

            if (decompile)
            {
                gltfExporter = new GltfModelExporter()
                {
                    FileLoader = new TrackingFileLoader(exportData.VrfGuiContext.FileLoader),
                    ProgressReporter = new Progress<string>(SetProgress),
                };
            }
        }

        protected override void OnShown(EventArgs e)
        {
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
                            SetProgress($"Extracting {resourceType}s...");
                            await ExtractFilesAsync(files).ConfigureAwait(false);
                        }
                    }

                    if (filesToExtract.Count > 0)
                    {
                        SetProgress("Extracting files...");
                    }
                }

                await ExtractFilesAsync(filesToExtract).ConfigureAwait(false);
            }, cancellationTokenSource.Token).ContinueWith(ExportContinueWith, CancellationToken.None);
        }

        public void ExportContinueWith(Task t)
        {
            if (t.IsFaulted)
            {
                Console.Error.WriteLine(t.Exception);
                SetProgress(t.Exception.ToString());
            }

            Invoke(() =>
            {
                Text = "VRF - Export completed";
                cancelButton.Text = "Close";
                extractProgressBar.Value = 100;
                extractProgressBar.Style = ProgressBarStyle.Blocks;
                extractProgressBar.Update();
            });

            SetProgress("Export completed.");
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        public void QueueFiles(TreeNode root)
        {
            foreach (TreeNode node in root.Nodes)
            {
                var data = (VrfTreeViewData)node.Tag;
                if (!data.IsFolder)
                {
                    var file = data.PackageEntry;
                    if (decompile && filesToExtractSorted.TryGetValue(file.TypeName, out var specializedQueue))
                    {
                        specializedQueue.Enqueue(file);
                        continue;
                    }

                    filesToExtract.Enqueue(file);
                }
                else
                {
                    QueueFiles(node);
                }
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

                if (GltfModelExporter.CanExport(resource))
                {
                    outFilePath = Path.ChangeExtension(outFilePath, "glb");
                }

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

            try
            {
                contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext.FileLoader);

                if (contentFile.Data.Length > 0)
                {
                    SetProgress($"+ {outFilePath.Remove(0, path.Length + 1)}");
                    await File.WriteAllBytesAsync(outFilePath, contentFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                }

                string contentRelativeFolder;

                // Handle the subfiles of external refs directly
                if (contentFile.SubFilesAreExternal)
                {
                    foreach (var (refFileName, refContentFile) in contentFile.ExternalRefsHandled)
                    {
                        SetProgress($"Extracting {refFileName}");
                        extractedFiles.Add(refFileName);

                        contentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(refFileName);

                        await ExtractSubfiles(contentRelativeFolder, refContentFile).ConfigureAwait(false);
                    }
                    return;
                }

                extractedFiles.Add(inFilePath);
                foreach (var handledFile in contentFile.ExternalRefsHandled.Keys)
                {
                    extractedFiles.Add(handledFile);
                }

                contentRelativeFolder = flatSubfiles ? string.Empty : Path.GetDirectoryName(inFilePath);

                await ExtractSubfiles(contentRelativeFolder, contentFile).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Failed to extract '{inFilePath}': {e}").ConfigureAwait(false);
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
                var fullPath = Path.Combine(path, contentSubFile.FileName);

                if (extractedFiles.Contains(contentSubFile.FileName))
                {
                    SetProgress($"\t- {contentSubFile.FileName}");
                    continue;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(fullPath));

                byte[] subFileData;
                try
                {
                    subFileData = contentSubFile.Extract.Invoke();
                }
                catch (Exception e)
                {
                    await Console.Error.WriteLineAsync($"Failed to extract subfile '{contentSubFile.FileName}': {e}").ConfigureAwait(false);
                    SetProgress($"Failed to extract subfile '{contentSubFile.FileName}': {e.Message}");
                    continue;
                }

                if (subFileData.Length > 0)
                {
                    SetProgress($"\t+ {contentSubFile.FileName}");
                    extractedFiles.Add(contentSubFile.FileName);
                    await File.WriteAllBytesAsync(fullPath, subFileData, cancellationTokenSource.Token).ConfigureAwait(false);
                }
            }
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
                progressLog.AppendText($"[{DateTime.Now:HH:mm:ss.fff}] {text}{Environment.NewLine}");
            });
        }
    }
}

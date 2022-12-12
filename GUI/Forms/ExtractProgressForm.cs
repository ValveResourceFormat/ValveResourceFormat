using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Types.Exporter;
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
            // Materials before textures
            ResourceType.Material,
            ResourceType.Texture,
        };

        public Action<ExtractProgressForm> ShownCallback { get; init; }

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
                    FileLoader = exportData.VrfGuiContext.FileLoader,
                    ProgressReporter = new Progress<string>(SetProgress),
                };
            }
        }

        protected override void OnShown(EventArgs e)
        {
            if (ShownCallback != null)
            {
                ShownCallback(this);
                return;
            }

            Task
                .Run(
                async () =>
                {
                    Console.WriteLine($"Folder export started to \"{path}\"");

                    if (decompile)
                    {
                        foreach (var resourceType in ExtractOrder)
                        {
                            SetProgress($"Extracting {resourceType}s...");
                            var extension = FileExtract.GetExtension(resourceType);
                            await ExtractFilesAsync(filesToExtractSorted[extension + "_c"]).ConfigureAwait(false);
                        }

                        SetProgress("Extracting files...");
                    }

                    await ExtractFilesAsync(filesToExtract).ConfigureAwait(false);
                },
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (t.IsFaulted)
                    {
                        Console.WriteLine(t.Exception);
                    }

                    if (!t.IsCanceled)
                    {
                        Invoke(Close);
                    }
                });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        public void QueueFiles(TreeNode root)
        {
            foreach (TreeNode node in root.Nodes)
            {
                if (node.Tag is PackageEntry file)
                {
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

                exportData.VrfGuiContext.CurrentPackage.ReadEntry(packageFile, out var output, false);

                var outFilePath = Path.Combine(path, packageFile.GetFullPath());
                var outFolder = Path.GetDirectoryName(outFilePath);

                Directory.CreateDirectory(outFolder);

                if (!decompile || !outFilePath.EndsWith("_c", StringComparison.Ordinal))
                {
                    // Extract as is
                    await File.WriteAllBytesAsync(outFilePath, output, cancellationTokenSource.Token).ConfigureAwait(false);
                    continue;
                }

                using var resource = new Resource
                {
                    FileName = packageFile.GetFullPath(),
                };
                using var memory = new MemoryStream(output);
                resource.Read(memory);

                if (GltfModelExporter.CanExport(resource))
                {
                    outFilePath = Path.ChangeExtension(outFilePath, "glb");
                }

                await ExtractFile(resource, packageFile.GetFullPath(), outFilePath).ConfigureAwait(false);
            }
        }

        public async Task ExtractFile(Resource resource, string inFilePath, string outFilePath)
        {
            if (GltfModelExporter.CanExport(resource) && Path.GetExtension(outFilePath) is ".glb" or ".gltf")
            {
                gltfExporter.Export(resource, outFilePath, cancellationTokenSource.Token);
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
                    Console.WriteLine($"+ {outFilePath.Remove(0, path.Length + 1)}");
                    await File.WriteAllBytesAsync(outFilePath, contentFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                }

                // Handle the subfiles of external refs directly
                if (contentFile.SubFilesAreExternal)
                {
                    foreach (var (refFileName, refContentFile) in contentFile.ExternalRefsHandled)
                    {
                        SetProgress($"Extracting {refFileName}");
                        extractedFiles.Add(refFileName);
                        await ExtractSubfiles(Path.GetDirectoryName(refFileName), refContentFile).ConfigureAwait(false);
                    }
                    return;
                }

                extractedFiles.Add(inFilePath);
                foreach (var handledFile in contentFile.ExternalRefsHandled.Keys)
                {
                    extractedFiles.Add(handledFile);
                }

                await ExtractSubfiles(Path.GetDirectoryName(inFilePath), contentFile).ConfigureAwait(false);
            }
            catch (Exception e)
            {
                await Console.Error.WriteLineAsync($"Failed to extract '{inFilePath}': {e}").ConfigureAwait(false);
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
                    Console.WriteLine($"\t- {contentSubFile.FileName}");
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
                    continue;
                }

                if (subFileData.Length > 0)
                {
                    Console.WriteLine($"\t+ {contentSubFile.FileName}");
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
            Invoke(() =>
            {
                extractStatusLabel.Text = text;
            });
        }
    }
}

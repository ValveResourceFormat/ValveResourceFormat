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
using Exception = System.Exception;

namespace GUI.Forms
{
    public partial class ExtractProgressForm : Form
    {
        private readonly Package package;
        private readonly TreeNode root;
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

        public bool Decompile => exportData != null;

        public ExtractProgressForm(Package package, TreeNode root, string path, ExportData exportData = null)
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

            this.package = package;
            this.root = root;
            this.path = path;
            this.exportData = exportData;

            if (Decompile)
            {
                gltfExporter = new GltfModelExporter()
                {
                    FileLoader = exportData.VrfGuiContext.FileLoader,
                };
            }
        }

        protected override void OnShown(EventArgs e)
        {
            Task
                .Run(
                async () =>
                {
                    Invoke((Action)(() =>
                    {
                        extractStatusLabel.Text = "Calculating...";
                        extractProgressBar.Style = ProgressBarStyle.Marquee;
                    }));

                    Console.WriteLine($"Folder export started to \"{path}\"");
                    CalculateFilesToExtract(root);

                    Invoke((Action)(() =>
                    {
                        extractProgressBar.Style = ProgressBarStyle.Continuous;
                    }));

                    if (Decompile)
                    {
                        foreach (var resourceType in ExtractOrder)
                        {
                            Invoke(() => Text = $"Extracting {resourceType}s...");
                            var extension = FileExtract.GetExtension(resourceType);
                            await ExtractFilesAsync(filesToExtractSorted[extension + "_c"]).ConfigureAwait(false);
                        }

                        Invoke(() => Text = $"Extracting files...");
                    }

                    await ExtractFilesAsync(filesToExtract).ConfigureAwait(false);
                },
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (!t.IsCanceled)
                    {
                        Invoke((Action)Close);
                    }
                });
        }

        protected override void OnClosing(CancelEventArgs e)
        {
            cancellationTokenSource.Cancel();
        }

        private void CalculateFilesToExtract(TreeNode root)
        {
            cancellationTokenSource.Token.ThrowIfCancellationRequested();

            foreach (TreeNode node in root.Nodes)
            {
                if (node.Tag.GetType() == typeof(PackageEntry))
                {
                    var file = node.Tag as PackageEntry;

                    if (Decompile && filesToExtractSorted.TryGetValue(file.TypeName, out var specializedQueue))
                    {
                        specializedQueue.Enqueue(file);
                        continue;
                    }

                    filesToExtract.Enqueue(file);
                }
                else
                {
                    CalculateFilesToExtract(node);
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

                Invoke((Action)(() =>
                {
                    extractProgressBar.Value = 100 - (int)(filesToExtract.Count / (float)initialCount * 100.0f);
                    extractStatusLabel.Text = $"Extracting {packageFile.GetFullPath()}";
                }));

                var outFilePath = Path.Combine(path, packageFile.GetFullPath());
                var outFolder = Path.GetDirectoryName(outFilePath);

                package.ReadEntry(packageFile, out var output, false);

                Directory.CreateDirectory(outFolder);

                if (Decompile && outFilePath.EndsWith("_c", StringComparison.Ordinal))
                {
                    ContentFile contentFile = null;

                    using (var resource = new Resource
                    {
                        FileName = packageFile.GetFullPath(),
                    })
                    using (var memory = new MemoryStream(output))
                    {
                        try
                        {
                            resource.Read(memory);

                            if (GltfModelExporter.CanExport(resource))
                            {
                                gltfExporter.Export(resource, Path.ChangeExtension(outFilePath, "glb"));
                                continue;
                            }

                            var extension = FileExtract.GetExtension(resource);

                            if (extension == null)
                            {
                                outFilePath = outFilePath[..^2];
                            }
                            else
                            {
                                outFilePath = Path.ChangeExtension(outFilePath, extension);
                            }

                            contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext.FileLoader);
                        }
                        catch (Exception e)
                        {
                            await Console.Error.WriteLineAsync($"Failed to extract '{packageFile.GetFullPath()}' - {e.Message}").ConfigureAwait(false);
                            contentFile?.Dispose();
                            continue;
                        }
                    }

                    using (contentFile)
                    {
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
                                Invoke(() => extractStatusLabel.Text = $"Extracting {refFileName}");
                                extractedFiles.Add(refFileName);
                                await ExtractSubfiles(Path.GetDirectoryName(refFileName), refContentFile).ConfigureAwait(false);
                            }
                            continue;
                        }

                        extractedFiles.Add(packageFile.GetFullPath());
                        foreach (var handledFile in contentFile.ExternalRefsHandled.Keys)
                        {
                            extractedFiles.Add(handledFile);
                        }

                        await ExtractSubfiles(Path.GetDirectoryName(packageFile.GetFullPath()), contentFile).ConfigureAwait(false);
                    }

                    continue;
                }

                // Extract as is
                await File.WriteAllBytesAsync(outFilePath, output, cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }

        private async Task ExtractSubfiles(string contentRelativeFolder, ContentFile contentFile)
        {
            foreach (var contentSubFile in contentFile.SubFiles)
            {
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
                    await Console.Error.WriteLineAsync($"Failed to extract subfile '{contentSubFile.FileName}' - {e.Message}").ConfigureAwait(false);
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
    }
}

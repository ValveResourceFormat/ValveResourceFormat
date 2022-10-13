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
        private readonly Queue<PackageEntry> filesToExtract;
        private readonly GltfModelExporter gltfExporter;
        private CancellationTokenSource cancellationTokenSource;
        private int initialFileCount;

        public bool Decompile => exportData != null;

        public ExtractProgressForm(Package package, TreeNode root, string path, ExportData exportData = null)
        {
            InitializeComponent();

            cancellationTokenSource = new CancellationTokenSource();
            filesToExtract = new Queue<PackageEntry>();
            initialFileCount = 0;

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

                    CalculateFilesToExtract(root);
                    initialFileCount = filesToExtract.Count;

                    Invoke((Action)(() =>
                    {
                        extractProgressBar.Style = ProgressBarStyle.Continuous;
                    }));

                    await ExtractFilesAsync().ConfigureAwait(false);
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
                    filesToExtract.Enqueue(file);
                }
                else
                {
                    CalculateFilesToExtract(node);
                }
            }
        }

        private async Task ExtractFilesAsync()
        {
            while (filesToExtract.Count > 0)
            {
                cancellationTokenSource.Token.ThrowIfCancellationRequested();

                var packageFile = filesToExtract.Dequeue();

                Invoke((Action)(() =>
                {
                    extractProgressBar.Value = 100 - (int)((filesToExtract.Count / (float)initialFileCount) * 100.0f);
                    extractStatusLabel.Text = $"Extracting {packageFile.GetFullPath()}";
                }));

                var outFilePath = Path.Combine(path, packageFile.GetFullPath());
                var outFolder = Path.GetDirectoryName(outFilePath);

                package.ReadEntry(packageFile, out var output, false);

                Directory.CreateDirectory(outFolder);

                if (Decompile && outFilePath.EndsWith("_c", StringComparison.Ordinal))
                {
                    ContentFile contentFile;

                    using (var resource = new Resource
                    {
                        FileName = outFilePath,
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
                                outFilePath = outFilePath.Substring(0, outFilePath.Length - 2);
                            }
                            else
                            {
                                outFilePath = Path.ChangeExtension(outFilePath, extension);
                            }

                            contentFile = FileExtract.Extract(resource);
                        }
                        catch (Exception e)
                        {
                            await Console.Error.WriteLineAsync($"Failed to extract '{packageFile.GetFullPath()}' - {e.Message}").ConfigureAwait(false);
                            continue;
                        }
                    }

                    if (contentFile.Data.Length > 0)
                    {
                        Console.WriteLine($"Writing content file: {outFilePath}");
                        await File.WriteAllBytesAsync(outFilePath, contentFile.Data, cancellationTokenSource.Token).ConfigureAwait(false);
                    }

                    foreach (var contentSubFile in contentFile.SubFiles)
                    {
                        var subFilePath = Path.Combine(outFolder, contentSubFile.FileName);
                        Directory.CreateDirectory(Path.GetDirectoryName(subFilePath));

                        byte[] subFileData;
                        try
                        {
                            subFileData = contentSubFile.Extract();
                        }
                        catch (Exception e)
                        {
                            await Console.Error.WriteLineAsync($"Failed to extract subfile '{contentSubFile.FileName}' - {e.Message}").ConfigureAwait(false);
                            continue;
                        }

                        if (subFileData.Length > 0)
                        {
                            Console.WriteLine($"Writing content subfile: {subFilePath}");
                            await File.WriteAllBytesAsync(subFilePath, subFileData, cancellationTokenSource.Token).ConfigureAwait(false);
                        }
                    }

                    continue;
                }

                // Extract as is
                await File.WriteAllBytesAsync(outFilePath, output, cancellationTokenSource.Token).ConfigureAwait(false);
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}

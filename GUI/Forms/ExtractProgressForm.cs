using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
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
        private readonly bool decompile;
        private readonly Queue<PackageEntry> filesToExtract;
        private CancellationTokenSource cancellationTokenSource;
        private int initialFileCount;

        private GltfModelExporter exporter;

        public ExtractProgressForm(Package package, TreeNode root, string path, bool decompile)
        {
            InitializeComponent();

            cancellationTokenSource = new CancellationTokenSource();
            filesToExtract = new Queue<PackageEntry>();
            initialFileCount = 0;

            this.package = package;
            this.root = root;
            this.path = path;
            this.decompile = decompile;

            exporter = new GltfModelExporter
            {
                FileLoader = new BasicVpkFileLoader(package)
            };
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

                var filePath = Path.Combine(path, packageFile.GetFullPath());

                package.ReadEntry(packageFile, out var output, false);

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                if (decompile && filePath.EndsWith("_c", StringComparison.Ordinal))
                {
                    using (var resource = new Resource
                    {
                        FileName = filePath,
                    })
                    using (var memory = new MemoryStream(output))
                    {
                        try
                        {
                            resource.Read(memory);

                            var extension = FileExtract.GetExtension(resource);

                            if (extension == null)
                            {
                                filePath = filePath.Substring(0, filePath.Length - 2);
                            }
                            else
                            {
                                filePath = Path.ChangeExtension(filePath, extension);
                            }

                            output = FileExtract.Extract(resource, exporter, filePath).ToArray();
                        }
                        catch (Exception e)
                        {
                            Console.WriteLine($"Failed to extract '{packageFile.GetFullPath()}' - {e.Message}");
                            continue;
                        }
                    }
                }

                if (output.Length > 0)
                {
                    var stream = new FileStream(filePath, FileMode.Create);
                    await using (stream.ConfigureAwait(false))
                    {
                        await stream.WriteAsync(output, cancellationTokenSource.Token).ConfigureAwait(false);
                    }
                }
                else
                {
                    Console.WriteLine("Skip write" + filePath);
                }
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}

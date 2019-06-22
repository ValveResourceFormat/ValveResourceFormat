using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using System.Windows.Forms;
using SteamDatabase.ValvePak;

namespace GUI.Forms
{
    public partial class ExtractProgressForm : Form
    {
        private CancellationTokenSource cancellationTokenSource;
        private Package package;
        private TreeNode root;
        private string path;
        private Queue<PackageEntry> filesToExtract;
        private int initialFileCount;

        public ExtractProgressForm(Package package, TreeNode root, string path)
        {
            InitializeComponent();

            cancellationTokenSource = new CancellationTokenSource();
            filesToExtract = new Queue<PackageEntry>();
            initialFileCount = 0;

            this.package = package;
            this.root = root;
            this.path = path;
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

                    await ExtractFilesAsync();
                },
                cancellationTokenSource.Token)
                .ContinueWith((t) =>
                {
                    if (!t.IsCanceled)
                    {
                        Invoke((Action)(() => Close()));
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
                    extractProgressBar.Value = 100 - (int)(((float)filesToExtract.Count / (float)initialFileCount) * 100.0f);
                    extractStatusLabel.Text = $"Extracting {packageFile.GetFullPath()}";
                }));

                var filePath = Path.Combine(path, packageFile.GetFullPath());

                Directory.CreateDirectory(Path.GetDirectoryName(filePath));

                using (var stream = new FileStream(filePath, FileMode.Create))
                {
                    package.ReadEntry(packageFile, out var output);
                    await stream.WriteAsync(output, 0, output.Length, cancellationTokenSource.Token);
                }
            }
        }

        private void CancelButton_Click(object sender, EventArgs e)
        {
            Close();
        }
    }
}

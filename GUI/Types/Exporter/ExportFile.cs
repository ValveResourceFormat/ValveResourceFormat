using System;
using System.IO;
using System.Threading.Tasks;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using SteamDatabase.ValvePak;
using ValveResourceFormat.IO;
using Resource = ValveResourceFormat.Resource;

namespace GUI.Types.Exporter
{
    public static class ExportFile
    {
        public static void Export(string fileName, byte[] output, ExportData exportData)
        {
            var resource = new Resource
            {
                FileName = fileName,
            };
            var memory = new MemoryStream(output);
            resource.Read(memory);

            var extension = FileExtract.GetExtension(resource);

            if (extension == null)
            {
                Console.WriteLine($"Export for \"{fileName}\" has no suitable extension");
                return;
            }

            var filter = $"{extension} file|*.{extension}";

            if (GltfModelExporter.CanExport(resource))
            {
                var gltfFilter = "glTF|*.gltf";
                var glbFilter = "glTF Binary|*.glb";

                filter = $"{gltfFilter}|{glbFilter}|{filter}";
            }

            using var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileNameWithoutExtension(fileName),
                InitialDirectory = Settings.Config.SaveDirectory,
                DefaultExt = extension,
                Filter = filter,
            };

            var result = dialog.ShowDialog();

            if (result != DialogResult.OK)
            {
                return;
            }

            var directory = Path.GetDirectoryName(dialog.FileName);
            Settings.Config.SaveDirectory = directory;
            Settings.Save();

            var extractDialog = new ExtractProgressForm(exportData, directory, true)
            {
                ShownCallback = (form) =>
                {
                    form.SetProgress($"Extracting {fileName} to \"{Path.GetFileName(dialog.FileName)}\"");

                    Task.Run(async () =>
                    {
                        await form.ExtractFile(resource, fileName, dialog.FileName).ConfigureAwait(false);
                    }).ContinueWith(t =>
                    {
                        memory.Dispose();
                        resource.Dispose();
                        form.Invoke(form.Close);
                    });
                }
            };
            extractDialog.ShowDialog();
        }

        public static void ExtractFileFromPackageEntry(PackageEntry file, VrfGuiContext vrfGuiContext, bool decompile)
        {
            vrfGuiContext.CurrentPackage.ReadEntry(file, out var output, validateCrc: file.CRC32 > 0);

            ExtractFileFromByteArray(file.GetFileName(), output, vrfGuiContext, decompile);
        }

        public static void ExtractFileFromByteArray(string fileName, byte[] output, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (decompile && fileName.EndsWith("_c", StringComparison.Ordinal))
            {
                Export(fileName, output, new ExportData
                {
                    VrfGuiContext = new VrfGuiContext(null, vrfGuiContext),
                });

                return;
            }

            var dialog = new SaveFileDialog
            {
                InitialDirectory = Settings.Config.SaveDirectory,
                Filter = "All files (*.*)|*.*",
                FileName = fileName,
            };
            var userOK = dialog.ShowDialog();

            if (userOK == DialogResult.OK)
            {
                Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
                Settings.Save();

                Console.WriteLine($"Saved \"{Path.GetFileName(dialog.FileName)}\"");

                using var stream = dialog.OpenFile();
                stream.Write(output, 0, output.Length);
            }
        }

        public static void ExtractFilesFromTreeNode(TreeNode selectedNode, VrfGuiContext vrfGuiContext, bool decompile)
        {
            if (selectedNode.Tag is PackageEntry file)
            {
                // We are a file
                ExtractFileFromPackageEntry(file, vrfGuiContext, decompile);
            }
            else
            {
                // We are a folder
                using var dialog = new FolderBrowserDialog
                {
                    InitialDirectory = Settings.Config.SaveDirectory,
                };

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                Settings.Config.SaveDirectory = dialog.SelectedPath;
                Settings.Save();

                var exportData = new ExportData
                {
                    VrfGuiContext = vrfGuiContext,
                };

                var extractDialog = new ExtractProgressForm(exportData, dialog.SelectedPath, decompile);
                extractDialog.QueueFiles(selectedNode);
                extractDialog.ShowDialog();
            }
        }
    }
}

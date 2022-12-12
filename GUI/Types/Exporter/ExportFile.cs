using System;
using System.IO;
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
        public static void Export(string fileName, ExportData exportData)
        {
            var resource = exportData.Resource;
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
                Console.WriteLine($"Export for \"{fileName}\" cancelled");
                return;
            }

            Console.WriteLine($"Export for \"{fileName}\" started to \"{Path.GetFileName(dialog.FileName)}\"");

            Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
            Settings.Save();

            var extractDialog = new GenericProgressForm();
            extractDialog.OnProcess += (_, __) =>
            {
                if (GltfModelExporter.CanExport(resource) && dialog.FilterIndex <= 2)
                {
                    var exporter = new GltfModelExporter
                    {
                        ProgressReporter = new Progress<string>(extractDialog.SetProgress),
                        FileLoader = exportData.VrfGuiContext.FileLoader,
                    };

                    exporter.Export(resource, dialog.FileName, null);
                }
                else
                {
                    using var contentFile = FileExtract.Extract(resource, exportData.VrfGuiContext?.FileLoader);
                    using var stream = dialog.OpenFile();
                    stream.Write(contentFile.Data);

                    foreach (var contentSubFile in contentFile.SubFiles)
                    {
                        Console.WriteLine($"Export for \"{fileName}\" also writing \"{contentSubFile.FileName}\"");
                        var subFilePath = Path.Combine(Path.GetDirectoryName(dialog.FileName), contentSubFile.FileName);
                        using var subFileStream = File.OpenWrite(subFilePath);
                        subFileStream.Write(contentSubFile.Extract.Invoke());
                    }
                }

                Console.WriteLine($"Export for \"{fileName}\" completed");
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
                using var resource = new Resource
                {
                    FileName = fileName,
                };
                using var memory = new MemoryStream(output);

                resource.Read(memory);

                Export(fileName, new ExportData
                {
                    Resource = resource,
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
                using var dialog = new FolderBrowserDialog();

                if (dialog.ShowDialog() != DialogResult.OK)
                {
                    return;
                }

                ExportData exportData = null;
                if (decompile)
                {
                    exportData = new ExportData
                    {
                        VrfGuiContext = new VrfGuiContext(null, vrfGuiContext),
                    };
                }

                var extractDialog = new ExtractProgressForm(vrfGuiContext.CurrentPackage, selectedNode, dialog.SelectedPath, exportData);
                extractDialog.ShowDialog();
            }
        }
    }
}

using System;
using System.IO;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

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

            if (resource.ResourceType == ResourceType.Mesh || resource.ResourceType == ResourceType.Model)
            {
                if (exportData.FileType == ExportFileType.GLB)
                {
                    extension = "glb";
                    filter = $"GLB file|*.glb|{filter}";
                }
                else
                {
                    extension = "gltf";
                    filter = $"glTF file|*.gltf|{filter}";
                }
            }

            var dialog = new SaveFileDialog
            {
                FileName = Path.GetFileName(Path.ChangeExtension(fileName, extension)),
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

            Console.WriteLine($"Export for \"{fileName}\" started to \"{extension}\"");

            Settings.Config.SaveDirectory = Path.GetDirectoryName(dialog.FileName);
            Settings.Save();

            var extractDialog = new GenericProgressForm();
            extractDialog.OnProcess += (_, __) =>
            {
                if (resource.ResourceType == ResourceType.Mesh && dialog.FilterIndex == 1)
                {
                    var exporter = new GltfModelExporter
                    {
                        ProgressReporter = new Progress<string>(extractDialog.SetProgress),
                        FileLoader = exportData.VrfGuiContext.FileLoader,
                    };
                    exporter.ExportToFile(fileName, dialog.FileName, new Mesh(resource));
                }
                else if (resource.ResourceType == ResourceType.Model && dialog.FilterIndex == 1)
                {
                    var exporter = new GltfModelExporter
                    {
                        ProgressReporter = new Progress<string>(extractDialog.SetProgress),
                        FileLoader = exportData.VrfGuiContext.FileLoader,
                    };
                    exporter.ExportToFile(fileName, dialog.FileName, (Model)resource.DataBlock);
                }
                else
                {
                    var data = FileExtract.Extract(resource).ToArray();
                    using var stream = dialog.OpenFile();
                    stream.Write(data, 0, data.Length);
                }

                Console.WriteLine($"Export for \"{fileName}\" completed");
            };
            extractDialog.ShowDialog();
        }
    }
}

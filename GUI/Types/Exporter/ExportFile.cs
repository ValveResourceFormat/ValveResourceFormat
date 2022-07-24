using System;
using System.IO;
using System.Windows.Forms;
using GUI.Forms;
using GUI.Utils;
using ValveResourceFormat.IO;

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
                if (exportData.FileType == ExportFileType.GLB)
                {
                    extension = "glb";
                    filter = $"GLB file|*.glb|glTF file|*.gltf|{filter}";
                }
                else
                {
                    extension = "gltf";
                    filter = $"glTF file|*.gltf|GLB file|*.glb|{filter}";
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
                if (dialog.FilterIndex <= 2 && GltfModelExporter.CanExport(resource))
                {
                    var exporter = new GltfModelExporter
                    {
                        ProgressReporter = new Progress<string>(extractDialog.SetProgress),
                        FileLoader = exportData.VrfGuiContext.FileLoader,
                    };

                    exporter.Export(resource, dialog.FileName);
                }
                else
                {
                    var extractedResource = FileExtract.Extract(resource);
                    using var stream = dialog.OpenFile();
                    stream.Write(extractedResource.Data);

                    foreach (var childExtractedResource in extractedResource.Children)
                    {
                        var childFileName = Path.Combine(Path.GetDirectoryName(dialog.FileName), childExtractedResource.FileName);
                        using var childStream = File.OpenWrite(childFileName);
                        childStream.Write(childExtractedResource.Data);
                    }
                }

                Console.WriteLine($"Export for \"{fileName}\" completed");
            };
            extractDialog.ShowDialog();
        }
    }
}

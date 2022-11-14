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
                var gltfFilter = "glTF|*.gltf";
                var glbFilter = "glTF Binary|*.glb";

                filter = $"{gltfFilter}|{glbFilter}|{filter}";
            }

            var dialog = new SaveFileDialog
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

                    exporter.Export(resource, dialog.FileName);
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
    }
}

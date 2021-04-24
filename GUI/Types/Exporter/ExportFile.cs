using System;
using System.Collections.Generic;
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
        static ISet<ResourceType> ResourceTypesThatAreGltfExportable = new HashSet<ResourceType>()
        { ResourceType.Mesh, ResourceType.Model, ResourceType.WorldNode, ResourceType.World };

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

            if (ResourceTypesThatAreGltfExportable.Contains(resource.ResourceType))
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
                if (dialog.FilterIndex == 1 && ResourceTypesThatAreGltfExportable.Contains(resource.ResourceType))
                {
                    var exporter = new GltfModelExporter
                    {
                        ProgressReporter = new Progress<string>(extractDialog.SetProgress),
                        FileLoader = exportData.VrfGuiContext.FileLoader,
                    };
                    switch(resource.ResourceType)
                    {
                        case ResourceType.Mesh:
                            exporter.ExportToFile(fileName, dialog.FileName, new Mesh(resource));
                            break;
                        case ResourceType.Model:
                            exporter.ExportToFile(fileName, dialog.FileName, (Model)resource.DataBlock);
                            break;
                        case ResourceType.WorldNode:
                            exporter.ExportToFile(fileName, dialog.FileName, (WorldNode)resource.DataBlock);
                            break;
                        case ResourceType.World:
                            exporter.ExportToFile(fileName, dialog.FileName, (World)resource.DataBlock);
                            break;
                        default:
                            break;
                    }
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

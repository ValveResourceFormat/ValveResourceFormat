using System.IO;
using ValvePak;
using ValveResourceFormat.Source1.FileSystem;

namespace ValveResourceFormat.Source1.Model;

/// <summary>
/// Opens the files of a model that is being converted. They come from the same place as the model, since the
/// game can override them with another build.
/// </summary>
/// <param name="modelPath">The path of the model in the game.</param>
/// <param name="openModel">Opens the model file itself.</param>
/// <param name="package">The package the model is in, or <see langword="null"/> when it is a file on disk.</param>
/// <param name="directory">The directory of the model on disk.</param>
/// <param name="fileSystem">The file system of the game, which serves the files the model's place does not have.</param>
internal sealed class Source1ModelFileOpener(string modelPath, Func<Stream> openModel, Package? package, string? directory, Source1FileSystem? fileSystem)
{
    private readonly Source1FileSystem.AvailablePackage? availablePackage = package == null ? null : new(package, null);

    /// <summary>Opens a file of the model, or returns <see langword="null"/> when there is none.</summary>
    /// <param name="path">The path of the file in the game.</param>
    public Stream? Open(string path)
    {
        if (path.Equals(modelPath, StringComparison.OrdinalIgnoreCase))
        {
            return openModel();
        }

        var stream = OpenFromModelPlace(path);

        if (stream == null)
        {
            return fileSystem?.OpenFile(path);
        }

        fileSystem?.ReportOpened(path, stream);

        return stream;
    }

    private Stream? OpenFromModelPlace(string path)
    {
        if (availablePackage != null)
        {
            var entry = availablePackage.FindAvailableEntry(path);

            if (entry == null)
            {
                return null;
            }

            return availablePackage.Package.GetMemoryMappedStreamIfPossible(entry);
        }

        if (directory == null)
        {
            return null;
        }

        var diskPath = Path.Combine(directory, Path.GetFileName(path));

        if (!File.Exists(diskPath))
        {
            return null;
        }

        return new FileStream(diskPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite);
    }
}

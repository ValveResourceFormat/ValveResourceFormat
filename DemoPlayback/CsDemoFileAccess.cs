using System.IO;
using ValveResourceFormat.IO;
using ValveResourceFormat.ResourceTypes;

namespace ValveResourceFormat.DemoPlayback;

/// <summary>
/// Wraps <see cref="IFileLoader"/> for demo player model resolution and game text file access.
/// </summary>
public sealed class CsDemoFileAccess
{
    private readonly IFileLoader fileLoader;
    private readonly HashSet<string> missingModelPaths = new(StringComparer.OrdinalIgnoreCase);

    public CsDemoFileAccess(IFileLoader fileLoader)
    {
        this.fileLoader = fileLoader;
    }

    public bool ModelExists(string modelPath)
    {
        if (missingModelPaths.Contains(modelPath))
        {
            return false;
        }

        if (fileLoader.LoadFileCompiled(modelPath)?.DataBlock is Model)
        {
            return true;
        }

        missingModelPaths.Add(modelPath);
        return false;
    }

    public Stream? OpenTextFile(string path)
    {
        if (fileLoader is not GameFileLoader gameFileLoader)
        {
            return null;
        }

        var foundFile = gameFileLoader.FindFile(path, logNotFound: false);

        if (foundFile.PathOnDisk != null)
        {
            return File.OpenRead(foundFile.PathOnDisk);
        }

        if (foundFile.Package != null && foundFile.PackageEntry != null)
        {
            return GameFileLoader.GetPackageEntryStream(foundFile.Package, foundFile.PackageEntry);
        }

        return null;
    }
}

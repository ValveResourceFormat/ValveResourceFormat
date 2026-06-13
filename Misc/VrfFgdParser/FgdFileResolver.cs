using Sledge.Formats.FileSystem;

namespace VrfFgdParser;

public sealed class FgdFileResolver(string path) : IFileResolver
{
    private readonly string directory = Path.GetDirectoryName(path)!;

    Stream IFileResolver.OpenFile(string path)
    {
        var parent = Path.GetDirectoryName(directory);
        var paths = new List<string>
        {
            Path.Join(directory, path),
            Path.Join(parent, path),
            Path.Join(parent, "core", path),
        };

        foreach (var fullpath in paths)
        {
            if (File.Exists(fullpath))
            {
                return File.Open(fullpath, FileMode.Open, FileAccess.Read, FileShare.Read);
            }
        }

        Console.WriteLine($"Failed to find '{path}'");

        return Stream.Null;
    }

    bool IFileResolver.FileExists(string path)
    {
        throw new NotImplementedException();
    }

    bool IFileResolver.FolderExists(string path)
    {
        throw new NotImplementedException();
    }

    long IFileResolver.FileSize(string path)
    {
        throw new NotImplementedException();
    }

    IEnumerable<string> IFileResolver.GetFiles(string path)
    {
        throw new NotImplementedException();
    }

    IEnumerable<string> IFileResolver.GetFolders(string path)
    {
        throw new NotImplementedException();
    }
}
